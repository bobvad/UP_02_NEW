using API_UP_02.Models;
using API_UP_02.Context;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;

namespace API_UP_02.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "v3")]
    public class ParsingBooksController : Controller
    {
        private const string LitmirBaseUrl = "https://litmir.club";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

        private readonly BooksContext _context;

        public ParsingBooksController(BooksContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Получает указанное количество книг с Litmir.club
        /// </summary>
        /// <param name="count">Количество книг для парсинга (по умолчанию 200)</param>
        /// <returns>Список спарсенных книг</returns>
        [HttpGet("books/many")]
        public IActionResult GetManyBooks([FromQuery] int count = 200)
        {
            try
            {
                var books = ParseManyLitmirBooks(count);

                foreach (var book in books.Where(b => b.Id > 0))
                {
                    book.Content = ParseLitmirBookContent(book.ReadUrl);
                }

                var validBooks = books.Where(b => b.Id > 0).ToList();
                SaveBooksToDatabase(validBooks);
                return Ok(validBooks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new List<Book>());
            }
        }

        /// <summary>
        /// Получает книги с нескольких страниц Litmir.club
        /// </summary>
        /// <param name="pages">Количество страниц для парсинга (по умолчанию 5)</param>
        /// <returns>Список спарсенных книг</returns>
        [HttpGet("books/pages")]
        public IActionResult GetBooksFromPages([FromQuery] int pages = 5)
        {
            try
            {
                var books = ParseLitmirBooksFromMultiplePages(pages);

                foreach (var book in books.Where(b => b.Id > 0))
                {
                    book.Content = ParseLitmirBookContent(book.ReadUrl);
                }

                var validBooks = books.Where(b => b.Id > 0).ToList();
                SaveBooksToDatabase(validBooks);
                return Ok(validBooks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new List<Book>());
            }
        }

        /// <summary>
        /// Получает текст книги с Litmir.club по URL
        /// </summary>
        /// <param name="bookUrl">URL книги на Litmir.club</param>
        /// <returns>Текст книги</returns>
        [HttpGet("book/content")]
        public IActionResult GetBookContent([FromQuery] string bookUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(bookUrl))
                    return BadRequest("Не указан URL книги");

                var content = ParseLitmirBookContent(bookUrl);
                return Ok(new { Url = bookUrl, Content = content });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new { Error = ex.Message, Content = "" });
            }
        }

        /// <summary>
        /// Получает полную информацию о книге с Litmir.club по URL
        /// </summary>
        /// <param name="bookUrl">URL книги на Litmir.club</param>
        /// <returns>Объект книги с полной информацией</returns>
        [HttpGet("book/full")]
        public IActionResult GetFullBookInfo([FromQuery] string bookUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(bookUrl))
                    return BadRequest("Не указан URL книги");

                var book = ParseFullLitmirBookInfo(bookUrl);

                if (book.Id == 0)
                {
                    return BadRequest("Не удалось получить корректный ID книги");
                }

                return Ok(book);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Получает текст книги с Litmir.club по ID
        /// </summary>
        /// <param name="bookId">ID книги на Litmir.club</param>
        /// <returns>Текст книги</returns>
        [HttpGet("book/{bookId}/content")]
        public IActionResult GetBookContentById(int bookId)
        {
            try
            {
                if (bookId <= 0)
                    return BadRequest("Некорректный ID книги");

                var bookUrl = $"{LitmirBaseUrl}/br/?b={bookId}";
                var content = ParseLitmirBookContent(bookUrl);

                var book = _context.Books.Find(bookId);
                if (book != null)
                {
                    book.Content = content;
                    _context.SaveChanges();
                }

                return Ok(new { BookId = bookId, Content = content });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new { Error = ex.Message, Content = "" });
            }
        }

        /// <summary>
        /// Читает книгу с Litmir.club постранично
        /// </summary>
        /// <param name="bookId">ID книги на Litmir.club</param>
        /// <param name="page">Номер страницы (необязательный)</param>
        /// <returns>Текст страницы и навигация</returns>
        [HttpGet("book/{bookId}/read")]
        public IActionResult ReadBook(int bookId, [FromQuery] int? page = null)
        {
            try
            {
                if (bookId <= 0)
                    return BadRequest("Некорректный ID книги");

                var bookUrl = page.HasValue
                    ? $"{LitmirBaseUrl}/br/?b={bookId}&p={page.Value}"
                    : $"{LitmirBaseUrl}/br/?b={bookId}";

                var content = ParseLitmirBookContent(bookUrl);
                var navigation = GetLitmirBookNavigation(bookUrl);

                return Ok(new
                {
                    BookId = bookId,
                    Page = page ?? 1,
                    Navigation = navigation,
                    Content = content
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new { Error = ex.Message, Content = "" });
            }
        }

        /// <summary>
        /// Получает книги по жанру из базы данных или с сайта
        /// </summary>
        /// <param name="genre">Название жанра</param>
        /// <param name="count">Количество книг (по умолчанию 20)</param>
        /// <returns>Список книг указанного жанра</returns>
        [HttpGet("books/genre/{genre}")]
        public IActionResult GetBooksByGenre(string genre, [FromQuery] int count = 20)
        {
            try
            {
                var booksFromDb = _context.Books
                    .Where(b => b.Genre != null && b.Genre.Contains(genre))
                    .Take(count)
                    .ToList();

                if (booksFromDb.Count > 0)
                {
                    return Ok(booksFromDb);
                }

                var allBooks = ParseManyLitmirBooks(count * 2);
                var booksByGenre = allBooks
                    .Where(b => b.Id > 0 && b.Genre != null && b.Genre.Contains(genre, StringComparison.OrdinalIgnoreCase))
                    .Take(count)
                    .ToList();

                return Ok(booksByGenre);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new List<Book>());
            }
        }

        /// <summary>
        /// Получает книги по автору из базы данных или с сайта
        /// </summary>
        /// <param name="author">Имя автора</param>
        /// <param name="count">Количество книг (по умолчанию 20)</param>
        /// <returns>Список книг указанного автора</returns>
        [HttpGet("books/author/{author}")]
        public IActionResult GetBooksByAuthor(string author, [FromQuery] int count = 20)
        {
            try
            {
                var booksFromDb = _context.Books
                    .Where(b => b.Author != null && b.Author.Contains(author))
                    .Take(count)
                    .ToList();

                if (booksFromDb.Count > 0)
                {
                    return Ok(booksFromDb);
                }

                var allBooks = ParseManyLitmirBooks(count * 2);
                var booksByAuthor = allBooks
                    .Where(b => b.Id > 0 && b.Author != null && b.Author.Contains(author, StringComparison.OrdinalIgnoreCase))
                    .Take(count)
                    .ToList();

                return Ok(booksByAuthor);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new List<Book>());
            }
        }

        /// <summary>
        /// Получает все книги из базы данных
        /// </summary>
        /// <returns>Список книг из БД</returns>
        [HttpGet("database/books")]
        public async Task<IActionResult> GetBooksFromDatabase()
        {
            try
            {
                var books = await _context.Books.ToListAsync();
                return Ok(books);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Очищает таблицу книг в базе данных
        /// </summary>
        /// <returns>Результат операции</returns>
        [HttpDelete("database/clear")]
        public async Task<IActionResult> ClearDatabase()
        {
            try
            {
                _context.Books.RemoveRange(_context.Books);
                await _context.SaveChangesAsync();
                return Ok("База данных очищена");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Парсит указанное количество книг с сайта Readli.net
        /// </summary>
        /// <param name="count">Количество книг для парсинга (по умолчанию 100)</param>
        /// <param name="loadContent">Загружать ли текст книги (по умолчанию true)</param>
        /// <returns>Список спарсенных книг</returns>
        [HttpGet("readli/books")]
        public async Task<IActionResult> GetReadliBooks([FromQuery] int count = 100, [FromQuery] bool loadContent = true)
        {
            try
            {
                var books = new List<Book>();
                var web = CreateHtmlWeb();
                web.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                web.OverrideEncoding = Encoding.UTF8;

                int booksParsed = 0;

                var urlsToTry = new List<string>
                {
                    "https://readli.net/books/",
                    "https://readli.net/popular/",
                    "https://readli.net/last/",
                    "https://readli.net/genre/fantastika/",
                    "https://readli.net/genre/fentezi/",
                    "https://readli.net/genre/detektiv/",
                    "https://readli.net/genre/lyubovnye-romany/",
                    "https://readli.net/genre/priklyucheniya/",
                    "https://readli.net/genre/triller/",
                    "https://readli.net/genre/uzhasy/",
                };

                foreach (var baseUrl in urlsToTry)
                {
                    if (booksParsed >= count) break;

                    for (int page = 1; page <= 5; page++)
                    {
                        if (booksParsed >= count) break;

                        string pageUrl = page == 1 ? baseUrl : baseUrl.TrimEnd('/') + $"/page/{page}/";

                        try
                        {
                            Console.WriteLine($"Загрузка страницы: {pageUrl}");
                            var doc = web.Load(pageUrl);

                            var bookBlocks = doc.DocumentNode.SelectNodes("//div[contains(@class, 'book-item')]") ??
                                           doc.DocumentNode.SelectNodes("//article[contains(@class, 'book')]") ??
                                           doc.DocumentNode.SelectNodes("//div[contains(@class, 'post-card')]") ??
                                           doc.DocumentNode.SelectNodes("//div[contains(@class, 'book-card')]") ??
                                           doc.DocumentNode.SelectNodes("//div[contains(@class, 'book_row')]");

                            if (bookBlocks == null || bookBlocks.Count == 0)
                            {
                                Console.WriteLine("Блоки не найдены, ищем прямые ссылки...");
                                var bookLinks = doc.DocumentNode.SelectNodes("//a[contains(@href, '/book/')]");

                                if (bookLinks != null && bookLinks.Count > 0)
                                {
                                    var processedUrls = new HashSet<string>();

                                    foreach (var link in bookLinks)
                                    {
                                        if (booksParsed >= count) break;

                                        string href = link.GetAttributeValue("href", "");
                                        if (string.IsNullOrEmpty(href) || !href.Contains("/book/")) continue;

                                        string fullUrl = BuildFullUrl(href, "https://readli.net");
                                        if (processedUrls.Contains(fullUrl)) continue;
                                        processedUrls.Add(fullUrl);

                                        string title = WebUtility.HtmlDecode(link.InnerText.Trim());
                                        if (string.IsNullOrEmpty(title) || title.Length < 3) continue;

                                        var book = new Book
                                        {
                                            Title = title,
                                            BookUrl = fullUrl,
                                            Language = "Русский"
                                        };

                                        book.Id = ExtractReadliBookId(fullUrl);

                                        if (book.Id > 0)
                                        {
                                            book.ReadUrl = $"https://readli.net/chitat-online/?b={book.Id}";

                                            await EnrichReadliBookDetails(book, web);

                                            if (loadContent)
                                            {
                                                book.Content = await ParseReadliBookContentAsync(book.Id, web);
                                            }

                                            books.Add(book);
                                            booksParsed++;
                                            Console.WriteLine($"Добавлена книга {booksParsed}: {book.Title}, автор: {book.Author}, жанр: {book.Genre}");
                                        }
                                    }
                                }
                                continue;
                            }

                            Console.WriteLine($"Найдено блоков: {bookBlocks.Count}");

                            foreach (var block in bookBlocks)
                            {
                                if (booksParsed >= count) break;

                                try
                                {
                                    var book = new Book { Language = "Русский" };

                                    var titleLink = block.SelectSingleNode(".//a[contains(@href, '/book/')]") ??
                                                   block.SelectSingleNode(".//h2/a | .//h3/a | .//h4/a");

                                    if (titleLink == null) continue;

                                    book.Title = WebUtility.HtmlDecode(titleLink.InnerText.Trim());
                                    if (string.IsNullOrEmpty(book.Title) || book.Title.Length < 2) continue;

                                    string href = titleLink.GetAttributeValue("href", "");
                                    book.BookUrl = BuildFullUrl(href, "https://readli.net");
                                    book.Id = ExtractReadliBookId(book.BookUrl);

                                    if (book.Id == 0) continue;

                                    book.ReadUrl = $"https://readli.net/chitat-online/?b={book.Id}";

                                    var authorSelectors = new[]
                                    {
                                        ".//a[contains(@href, '/author/')]",
                                        ".//span[contains(@class, 'author')]/a",
                                        ".//div[contains(@class, 'author')]/a",
                                        ".//span[@class='author']",
                                        ".//div[@class='author']"
                                    };

                                    foreach (var selector in authorSelectors)
                                    {
                                        var authorNode = block.SelectSingleNode(selector);
                                        if (authorNode != null)
                                        {
                                            book.Author = WebUtility.HtmlDecode(authorNode.InnerText.Trim());
                                            break;
                                        }
                                    }

                                    var genreSelectors = new[]
                                    {
                                        ".//a[contains(@href, '/genre/')]",
                                        ".//span[contains(@class, 'genre')]/a",
                                        ".//div[contains(@class, 'genre')]/a"
                                    };

                                    var genres = new List<string>();
                                    foreach (var selector in genreSelectors)
                                    {
                                        var genreNodes = block.SelectNodes(selector);
                                        if (genreNodes != null)
                                        {
                                            foreach (var genreNode in genreNodes)
                                            {
                                                var genre = WebUtility.HtmlDecode(genreNode.InnerText.Trim());
                                                if (!string.IsNullOrEmpty(genre) && !genres.Contains(genre))
                                                    genres.Add(genre);
                                            }
                                        }
                                    }

                                    book.Genre = genres.Any() ? string.Join(", ", genres) : "Не указан";

                                    var descSelectors = new[]
                                    {
                                        ".//div[contains(@class, 'desc')]",
                                        ".//div[contains(@class, 'description')]",
                                        ".//div[contains(@class, 'annotation')]",
                                        ".//p[contains(@class, 'desc')]"
                                    };

                                    foreach (var selector in descSelectors)
                                    {
                                        var descBlock = block.SelectSingleNode(selector);
                                        if (descBlock != null)
                                        {
                                            book.Description = WebUtility.HtmlDecode(Regex.Replace(descBlock.InnerText.Trim(), @"\s+", " "));
                                            if (book.Description.Length > 500)
                                                book.Description = book.Description.Substring(0, 500) + "...";
                                            break;
                                        }
                                    }

                                    if (string.IsNullOrEmpty(book.Description))
                                        book.Description = "Описание отсутствует";

                                    var imgNode = block.SelectSingleNode(".//img");
                                    if (imgNode != null)
                                    {
                                        string imgUrl = imgNode.GetAttributeValue("src", "") ??
                                                       imgNode.GetAttributeValue("data-src", "") ??
                                                       imgNode.GetAttributeValue("data-original", "");

                                        if (!string.IsNullOrEmpty(imgUrl) && !imgUrl.Contains("data:image"))
                                            book.ImageUrl = BuildFullUrl(imgUrl, "https://readli.net");
                                    }

                                    if (string.IsNullOrEmpty(book.Author) || book.Author == "Неизвестен" || book.Genre == "Не указан")
                                    {
                                        await EnrichReadliBookDetails(book, web);
                                    }

                                    if (string.IsNullOrEmpty(book.Author)) book.Author = "Неизвестен";
                                    if (string.IsNullOrEmpty(book.Genre)) book.Genre = "Не указан";

                                    if (loadContent)
                                    {
                                        book.Content = await ParseReadliBookContentAsync(book.Id, web);
                                    }

                                    books.Add(book);
                                    booksParsed++;

                                    if (booksParsed % 10 == 0)
                                    {
                                        Console.WriteLine($"Прогресс: {booksParsed}/{count} книг");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Ошибка при парсинге блока: {ex.Message}");
                                }
                            }

                            await Task.Delay(1000);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка загрузки страницы {pageUrl}: {ex.Message}");
                        }
                    }
                }

                Console.WriteLine($"Парсинг Readli завершен. Получено книг: {books.Count}");

                if (books.Any())
                {
                    SaveBooksToDatabase(books);

                    var withAuthors = books.Count(b => b.Author != "Неизвестен");
                    var withGenres = books.Count(b => b.Genre != "Не указан");
                    var withPages = books.Count(b => b.PageCount.HasValue);

                    Console.WriteLine($"Статистика: всего книг {books.Count}, с авторами {withAuthors}, с жанрами {withGenres}, со страницами {withPages}");

                    return Ok(new
                    {
                        Message = $"Успешно спарсено {books.Count} книг",
                        Statistics = new
                        {
                            Total = books.Count,
                            WithAuthors = withAuthors,
                            WithGenres = withGenres,
                            WithPages = withPages
                        },
                        Books = books
                    });
                }

                return Ok(new { Message = "Не удалось найти книги", Books = new List<Book>() });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при парсинге Readli: {ex.Message}");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Получает текст книги с Readli.net по ID
        /// </summary>
        /// <param name="bookId">ID книги на Readli.net</param>
        /// <returns>Текст книги</returns>
        [HttpGet("readli/book/{bookId}/content")]
        public async Task<IActionResult> GetReadliBookContent(int bookId)
        {
            try
            {
                var web = CreateHtmlWeb();
                web.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

                var content = await ParseReadliBookContentAsync(bookId, web);

                var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == bookId || b.ReadUrl.Contains($"b={bookId}"));
                if (book != null)
                {
                    book.Content = content;
                    await _context.SaveChangesAsync();
                }

                return Ok(new { BookId = bookId, Content = content });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        private void SaveBooksToDatabase(List<Book> books)
        {
            int addedCount = 0;
            int updatedCount = 0;
            int skippedCount = 0;
            int errorCount = 0;

            var processedIds = new HashSet<int>();
            var processedTitles = new HashSet<string>();

            foreach (var book in books)
            {
                try
                {
                    if (book.Id > 0 && processedIds.Contains(book.Id))
                    {
                        skippedCount++;
                        continue;
                    }

                    if (string.IsNullOrEmpty(book.Title) || processedTitles.Contains(book.Title + book.Author))
                    {
                        skippedCount++;
                        continue;
                    }

                    var existingBook = _context.Books.FirstOrDefault(b =>
                        (b.Id > 0 && b.Id == book.Id) ||
                        (b.Title == book.Title && b.Author == book.Author));

                    if (existingBook == null)
                    {
                        book.Title = TruncateString(book.Title, 255) ?? "Без названия";
                        book.Author = TruncateString(book.Author, 100) ?? "Неизвестен";
                        book.Genre = TruncateString(book.Genre, 200) ?? "Не указан";
                        book.Description = TruncateString(book.Description, 2000) ?? "Описание отсутствует";
                        book.ImageUrl = TruncateString(book.ImageUrl, 500);
                        book.BookUrl = TruncateString(book.BookUrl, 500);
                        book.ReadUrl = TruncateString(book.ReadUrl, 500);
                        book.DownloadUrl = TruncateString(book.DownloadUrl, 500);
                        book.Language = TruncateString(book.Language, 50) ?? "Русский";
                        book.Content = book.Content;

                        _context.Books.Add(book);

                        if (book.Id > 0)
                            processedIds.Add(book.Id);
                        processedTitles.Add(book.Title + book.Author);
                        addedCount++;
                    }
                    else
                    {
                        bool hasChanges = false;

                        if (!string.IsNullOrEmpty(book.Description) && book.Description != "Описание отсутствует" && existingBook.Description != book.Description)
                        {
                            existingBook.Description = book.Description;
                            hasChanges = true;
                        }

                        if (!string.IsNullOrEmpty(book.Genre) && book.Genre != "Не указан" && existingBook.Genre != book.Genre)
                        {
                            existingBook.Genre = book.Genre;
                            hasChanges = true;
                        }

                        if (!string.IsNullOrEmpty(book.ImageUrl) && existingBook.ImageUrl != book.ImageUrl)
                        {
                            existingBook.ImageUrl = book.ImageUrl;
                            hasChanges = true;
                        }

                        if (!string.IsNullOrEmpty(book.Content) && existingBook.Content != book.Content)
                        {
                            existingBook.Content = book.Content;
                            hasChanges = true;
                        }

                        if (book.PageCount.HasValue && existingBook.PageCount != book.PageCount)
                        {
                            existingBook.PageCount = book.PageCount;
                            hasChanges = true;
                        }

                        if (existingBook.IsCompleted != book.IsCompleted)
                        {
                            existingBook.IsCompleted = book.IsCompleted;
                            hasChanges = true;
                        }

                        if (hasChanges)
                        {
                            updatedCount++;
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Console.WriteLine($"Ошибка при сохранении книги {book.Title}: {ex.Message}");
                }
            }

            try
            {
                _context.SaveChanges();
                Console.WriteLine($"Добавлено: {addedCount}, Обновлено: {updatedCount}, Пропущено: {skippedCount}, Ошибок: {errorCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка SaveChanges: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Внутренняя ошибка: {ex.InnerException.Message}");
                }
            }
        }

        private string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        }

        private List<Book> ParseManyLitmirBooks(int targetCount)
        {
            var allBooks = new List<Book>();
            var web = CreateHtmlWeb();
            var currentPage = 1;

            while (allBooks.Count < targetCount && currentPage <= 10)
            {
                var pageUrl = currentPage == 1 ? LitmirBaseUrl : $"{LitmirBaseUrl}/knigi?page={currentPage}";
                var doc = web.Load(pageUrl);
                var bookRows = FindLitmirBookRows(doc);

                if (bookRows == null || bookRows.Count == 0)
                    break;

                foreach (var row in bookRows)
                {
                    if (allBooks.Count >= targetCount)
                        break;

                    var book = ParseLitmirBook(row);
                    if (book != null && !string.IsNullOrEmpty(book.Title) && book.Id > 0)
                    {
                        allBooks.Add(book);
                    }
                }

                currentPage++;
            }

            return allBooks;
        }

        private List<Book> ParseLitmirBooksFromMultiplePages(int pagesCount)
        {
            var allBooks = new List<Book>();
            var web = CreateHtmlWeb();

            for (int pageNum = 1; pageNum <= pagesCount; pageNum++)
            {
                var pageUrl = pageNum == 1 ? LitmirBaseUrl : $"{LitmirBaseUrl}/knigi?page={pageNum}";
                var doc = web.Load(pageUrl);
                var bookRows = FindLitmirBookRows(doc);

                if (bookRows == null || bookRows.Count == 0)
                    continue;

                foreach (var row in bookRows)
                {
                    var book = ParseLitmirBook(row);
                    if (book != null && !string.IsNullOrEmpty(book.Title) && book.Id > 0)
                    {
                        allBooks.Add(book);
                    }
                }
            }

            return allBooks;
        }

        private Book ParseLitmirBook(HtmlNode row)
        {
            try
            {
                var book = new Book { };

                var linkNode = FindLitmirBookLink(row);
                if (linkNode == null) return null;

                book.Title = WebUtility.HtmlDecode(linkNode.InnerText.Trim());

                var href = linkNode.GetAttributeValue("href", "");
                book.BookUrl = BuildFullUrl(href, LitmirBaseUrl);

                book.Id = ExtractLitmirBookId(book.BookUrl);

                if (book.Id == 0) return null;

                if (book.Id > 0)
                {
                    book.ReadUrl = $"{LitmirBaseUrl}/br/?b={book.Id}";
                    book.DownloadUrl = $"{LitmirBaseUrl}/b/d/{book.Id}/fb2";
                }

                book.Author = ParseLitmirAuthor(row);
                book.ImageUrl = ParseLitmirImageUrl(row);
                book.Description = ParseLitmirDescription(row);
                book.Genre = ParseLitmirGenres(row);
                book.Language = "Русский";
                book.PageCount = ParseLitmirPageCount(row);
                book.IsCompleted = CheckLitmirIsCompleted(row);

                return book;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка парсинга книги: {ex.Message}");
                return null;
            }
        }

        private HtmlNode FindLitmirBookLink(HtmlNode row)
        {
            var selectors = new[]
            {
                ".//span[@itemprop='name']/parent::a",
                ".//span[@itemprop='name']",
                ".//div[@class='book_name']//a",
                ".//a[@class='book_name']",
                ".//a[contains(@href, '/bd/')]",
                ".//a[contains(@href, '?b=')]"
            };

            foreach (var selector in selectors)
            {
                var node = row.SelectSingleNode(selector);
                if (node != null)
                {
                    if (node.Name == "span")
                    {
                        var parentLink = node.ParentNode;
                        if (parentLink.Name == "a")
                            return parentLink;
                    }
                    return node;
                }
            }

            return null;
        }

        private string ParseLitmirAuthor(HtmlNode row)
        {
            var selectors = new[]
            {
                ".//a[contains(@href, '/a/?id=')]",
                ".//span[@class='desc2']//a",
                ".//div[@class='author']//a"
            };

            foreach (var selector in selectors)
            {
                var node = row.SelectSingleNode(selector);
                if (node != null)
                    return WebUtility.HtmlDecode(node.InnerText.Trim());
            }

            return "Неизвестен";
        }

        private string ParseLitmirImageUrl(HtmlNode row)
        {
            var selectors = new[]
            {
                ".//img[@class='lt32']",
                ".//img[@data-src]",
                ".//img[contains(@src, '/data/Book/')]",
                ".//img"
            };

            foreach (var selector in selectors)
            {
                var imgNode = row.SelectSingleNode(selector);
                if (imgNode != null)
                {
                    var url = imgNode.GetAttributeValue("data-src", "") ??
                             imgNode.GetAttributeValue("src", "");

                    if (!string.IsNullOrEmpty(url))
                        return BuildFullUrl(url, LitmirBaseUrl);
                }
            }

            return null;
        }

        private string ParseLitmirDescription(HtmlNode row)
        {
            var descNode = row.SelectSingleNode(".//div[@class='description']");
            if (descNode == null) return "Описание отсутствует";

            var cleanDesc = descNode.CloneNode(true);
            var techDivs = cleanDesc.SelectNodes(".//div[@style]");
            if (techDivs != null)
            {
                foreach (var div in techDivs)
                    div.Remove();
            }

            var text = Regex.Replace(cleanDesc.InnerText.Trim(), @"\s+", " ");
            return WebUtility.HtmlDecode(text);
        }

        private string ParseLitmirGenres(HtmlNode row)
        {
            try
            {
                var genreSpan = row.SelectSingleNode(".//span[@itemprop='genre']");
                if (genreSpan == null) return "Жанр не указан";

                var genres = new List<string>();
                var genreLinks = genreSpan.SelectNodes(".//a[not(contains(@id, 'more'))]");

                if (genreLinks != null)
                {
                    foreach (var link in genreLinks)
                    {
                        var genre = link.InnerText.Trim();
                        if (!string.IsNullOrEmpty(genre) && genre != "...")
                            genres.Add(genre);
                    }
                }

                var hiddenSpan = genreSpan.SelectSingleNode(".//span[contains(@id, 'genres_rest')]");
                if (hiddenSpan != null)
                {
                    var hiddenLinks = hiddenSpan.SelectNodes(".//a");
                    if (hiddenLinks != null)
                    {
                        foreach (var link in hiddenLinks)
                        {
                            var genre = link.InnerText.Trim();
                            if (!string.IsNullOrEmpty(genre))
                                genres.Add(genre);
                        }
                    }
                }

                return genres.Count > 0 ? string.Join(", ", genres) : "Жанр не указан";
            }
            catch
            {
                return "Ошибка парсинга жанров";
            }
        }

        private int? ParseLitmirPageCount(HtmlNode row)
        {
            var match = Regex.Match(row.InnerText, @"Страниц:\s*(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int pages))
                return pages;

            return null;
        }

        private bool CheckLitmirIsCompleted(HtmlNode row)
        {
            var text = row.InnerText;
            return text.Contains("Книга закончена") ||
                   text.Contains("Завершено") ||
                   text.Contains("Полный текст");
        }

        private string ParseLitmirBookContent(string bookUrl)
        {
            var web = CreateHtmlWeb();

            try
            {
                var doc = web.Load(bookUrl);
                var textContainer = doc.DocumentNode.SelectSingleNode("//div[@class='page_text']") ??
                                   doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'page_text')]") ??
                                   doc.DocumentNode.SelectSingleNode("//div[@id='book_text']");

                if (textContainer == null)
                    return "Текст книги не найден";

                RemoveUnwantedElements(textContainer);
                return CleanHtmlContent(textContainer.InnerHtml);
            }
            catch (Exception ex)
            {
                return $"Ошибка загрузки текста: {ex.Message}";
            }
        }

        private Book ParseFullLitmirBookInfo(string bookUrl)
        {
            var web = CreateHtmlWeb();

            try
            {
                var bookId = ExtractLitmirBookId(bookUrl);
                if (bookId == 0) return new Book { Id = 0, Title = "Ошибка", Description = "Не удалось извлечь ID книги" };

                var doc = web.Load(bookUrl);

                var book = new Book
                {
                    Id = bookId,
                    BookUrl = bookUrl,
                    ReadUrl = $"{LitmirBaseUrl}/br/?b={bookId}",
                    DownloadUrl = $"{LitmirBaseUrl}/b/d/{bookId}/fb2",
                    Language = "Русский"
                };

                var titleNode = doc.DocumentNode.SelectSingleNode("//h1[@itemprop='name']");
                book.Title = WebUtility.HtmlDecode(titleNode?.InnerText.Trim() ?? "Без названия");

                var authorNode = doc.DocumentNode.SelectSingleNode("//a[@itemprop='author']");
                book.Author = WebUtility.HtmlDecode(authorNode?.InnerText.Trim() ?? "Неизвестен");

                var genreNodes = doc.DocumentNode.SelectNodes("//span[@itemprop='genre']//a");
                if (genreNodes != null)
                {
                    var genres = genreNodes
                        .Select(g => WebUtility.HtmlDecode(g.InnerText.Trim()))
                        .Where(g => !string.IsNullOrEmpty(g) && g != "...")
                        .ToList();
                    book.Genre = string.Join(", ", genres);
                }

                var descNode = doc.DocumentNode.SelectSingleNode("//div[@itemprop='description']") ??
                              doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'description')]");
                book.Description = WebUtility.HtmlDecode(descNode?.InnerText.Trim() ?? "Описание отсутствует");

                var imgNode = doc.DocumentNode.SelectSingleNode("//img[@itemprop='image']") ??
                             doc.DocumentNode.SelectSingleNode("//img[contains(@src, '/data/Book/')]");
                if (imgNode != null)
                {
                    var imgUrl = imgNode.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(imgUrl))
                        book.ImageUrl = BuildFullUrl(imgUrl, LitmirBaseUrl);
                }

                var pageText = doc.DocumentNode.InnerText;
                var pagesMatch = Regex.Match(pageText, @"Страниц:\s*(\d+)");
                if (pagesMatch.Success && int.TryParse(pagesMatch.Groups[1].Value, out int pages))
                    book.PageCount = pages;

                book.IsCompleted = pageText.Contains("Книга закончена") ||
                                   pageText.Contains("Завершено") ||
                                   pageText.Contains("Полный текст");

                book.Content = ParseLitmirBookContent(book.ReadUrl);

                return book;
            }
            catch (Exception ex)
            {
                return new Book { Id = 0, Title = "Ошибка", Description = ex.Message, BookUrl = bookUrl };
            }
        }

        private object GetLitmirBookNavigation(string bookUrl)
        {
            var web = CreateHtmlWeb();

            try
            {
                var doc = web.Load(bookUrl);

                return new
                {
                    PreviousPage = doc.DocumentNode
                        .SelectSingleNode("//a[contains(text(), '←') or contains(text(), 'Предыдущая')]")
                        ?.GetAttributeValue("href", ""),
                    NextPage = doc.DocumentNode
                        .SelectSingleNode("//a[contains(text(), '→') or contains(text(), 'Следующая')]")
                        ?.GetAttributeValue("href", "")
                };
            }
            catch
            {
                return null;
            }
        }

        private int ExtractLitmirBookId(string url)
        {
            if (string.IsNullOrEmpty(url)) return 0;

            var patterns = new[]
            {
                @"[?&]b=(\d+)",
                @"/b[drs]/?\?b=(\d+)",
                @"/b/(\d+)",
                @"/book[-_]?(\d+)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(url, pattern, RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
                    return id;
            }

            var numbers = Regex.Matches(url, @"\d+");
            if (numbers.Count > 0)
            {
                var lastNumber = numbers[numbers.Count - 1].Value;
                if (int.TryParse(lastNumber, out int lastId))
                    return lastId;
            }

            return 0;
        }

        private HtmlNodeCollection FindLitmirBookBlocks(HtmlDocument doc)
        {
            return doc.DocumentNode.SelectNodes("//div[@class='book_block']") ??
                   doc.DocumentNode.SelectNodes("//div[contains(@class, 'book-item')]") ??
                   doc.DocumentNode.SelectNodes("//tr[.//span[@itemprop='name']]");
        }

        private HtmlNodeCollection FindLitmirBookRows(HtmlDocument doc)
        {
            return doc.DocumentNode.SelectNodes("//table[@class='']//tr") ??
                   doc.DocumentNode.SelectNodes("//tr[.//span[@itemprop='name']]") ??
                   doc.DocumentNode.SelectNodes("//div[contains(@class, 'book-item')]") ??
                   doc.DocumentNode.SelectNodes("//div[@class='book_block']");
        }

        private HtmlWeb CreateHtmlWeb()
        {
            return new HtmlWeb { UserAgent = UserAgent };
        }

        private string BuildFullUrl(string url, string baseUrl)
        {
            if (string.IsNullOrEmpty(url)) return url;

            if (url.StartsWith("//")) return "https:" + url;
            if (url.StartsWith("/")) return baseUrl + url;
            if (!url.StartsWith("http")) return baseUrl + "/" + url;

            return url;
        }

        private void RemoveUnwantedElements(HtmlNode container)
        {
            var removeSelectors = new[] { ".//script", ".//style", ".//ins", ".//div[contains(@class, 'ad')]" };

            foreach (var selector in removeSelectors)
            {
                var nodes = container.SelectNodes(selector);
                if (nodes != null)
                {
                    foreach (var node in nodes)
                        node.Remove();
                }
            }
        }

        private string CleanHtmlContent(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";

            html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<p[^>]*>", "\n\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<[^>]+>", "");
            html = WebUtility.HtmlDecode(html);
            html = Regex.Replace(html, @"[ \t]+", " ");
            html = Regex.Replace(html, @"\n\s*\n\s*\n", "\n\n");

            return html.Trim();
        }

        private async Task EnrichReadliBookDetails(Book book, HtmlWeb web)
        {
            try
            {
                if (string.IsNullOrEmpty(book.BookUrl)) return;

                Console.WriteLine($"Загрузка деталей для книги: {book.BookUrl}");
                var doc = web.Load(book.BookUrl);

                var authorSelectors = new[]
                {
                    "//a[contains(@href, '/author/')]",
                    "//span[contains(@class, 'author')]/a",
                    "//div[contains(@class, 'author')]/a",
                    "//span[@class='author']",
                    "//div[@class='author']",
                    "//p[contains(@class, 'author')]/a",
                    "//meta[@name='author']/@content",
                    "//a[@rel='author']"
                };

                foreach (var selector in authorSelectors)
                {
                    if (selector.Contains("@content"))
                    {
                        var metaNode = doc.DocumentNode.SelectSingleNode(selector);
                        if (metaNode != null)
                        {
                            book.Author = WebUtility.HtmlDecode(metaNode.GetAttributeValue("content", ""));
                            if (!string.IsNullOrEmpty(book.Author) && book.Author != "Неизвестен")
                                break;
                        }
                    }
                    else
                    {
                        var authorNode = doc.DocumentNode.SelectSingleNode(selector);
                        if (authorNode != null)
                        {
                            book.Author = WebUtility.HtmlDecode(authorNode.InnerText.Trim());
                            if (!string.IsNullOrEmpty(book.Author) && book.Author != "Неизвестен")
                                break;
                        }
                    }
                }

                var genreSelectors = new[]
                {
                    "//a[contains(@href, '/genre/')]",
                    "//span[contains(@class, 'genre')]/a",
                    "//div[contains(@class, 'genre')]/a",
                    "//meta[@property='book:genre']/@content",
                    "//meta[@name='genre']/@content"
                };

                var genres = new List<string>();
                foreach (var selector in genreSelectors)
                {
                    if (selector.Contains("@content"))
                    {
                        var metaNode = doc.DocumentNode.SelectSingleNode(selector);
                        if (metaNode != null)
                        {
                            var genre = metaNode.GetAttributeValue("content", "");
                            if (!string.IsNullOrEmpty(genre) && !genres.Contains(genre))
                                genres.Add(genre);
                        }
                    }
                    else
                    {
                        var genreNodes = doc.DocumentNode.SelectNodes(selector);
                        if (genreNodes != null)
                        {
                            foreach (var genreNode in genreNodes)
                            {
                                var genre = WebUtility.HtmlDecode(genreNode.InnerText.Trim());
                                if (!string.IsNullOrEmpty(genre) && !genres.Contains(genre))
                                    genres.Add(genre);
                            }
                        }
                    }
                }

                if (genres.Any())
                {
                    book.Genre = string.Join(", ", genres);
                }

                var pageSelectors = new[]
                {
                    "//span[contains(text(), 'Страниц')]/following-sibling::span",
                    "//span[contains(text(), 'Страниц')]/following-sibling::text()",
                    "//div[contains(text(), 'Страниц')]",
                    "//p[contains(text(), 'страниц')]",
                    "//li[contains(text(), 'страниц')]",
                    "//meta[@name='pages']/@content"
                };

                foreach (var selector in pageSelectors)
                {
                    if (selector.Contains("@content"))
                    {
                        var metaNode = doc.DocumentNode.SelectSingleNode(selector);
                        if (metaNode != null)
                        {
                            var pagesStr = metaNode.GetAttributeValue("content", "");
                            if (int.TryParse(pagesStr, out int pages))
                            {
                                book.PageCount = pages;
                                break;
                            }
                        }
                    }
                    else
                    {
                        var pageNode = doc.DocumentNode.SelectSingleNode(selector);
                        if (pageNode != null)
                        {
                            var text = pageNode.InnerText;
                            var match = Regex.Match(text, @"(\d+)");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out int pages))
                            {
                                book.PageCount = pages;
                                break;
                            }
                        }
                    }
                }

                var descSelectors = new[]
                {
                    "//div[@itemprop='description']",
                    "//div[contains(@class, 'description')]",
                    "//div[contains(@class, 'annotation')]",
                    "//meta[@name='description']/@content",
                    "//meta[@property='og:description']/@content"
                };

                foreach (var selector in descSelectors)
                {
                    if (selector.Contains("@content"))
                    {
                        var metaNode = doc.DocumentNode.SelectSingleNode(selector);
                        if (metaNode != null)
                        {
                            var desc = metaNode.GetAttributeValue("content", "");
                            if (!string.IsNullOrEmpty(desc))
                            {
                                book.Description = WebUtility.HtmlDecode(desc);
                                break;
                            }
                        }
                    }
                    else
                    {
                        var descNode = doc.DocumentNode.SelectSingleNode(selector);
                        if (descNode != null)
                        {
                            book.Description = WebUtility.HtmlDecode(Regex.Replace(descNode.InnerText.Trim(), @"\s+", " "));
                            break;
                        }
                    }
                }

                Console.WriteLine($"Детали для книги {book.Title}: автор='{book.Author}', жанр='{book.Genre}', страницы={book.PageCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обогащении книги {book.Title}: {ex.Message}");
            }
        }

        private async Task<string> ParseReadliBookContentAsync(int bookId, HtmlWeb web)
        {
            try
            {
                string contentUrl = $"https://readli.net/chitat-online/?b={bookId}";
                var doc = web.Load(contentUrl);

                var contentSelectors = new[]
                {
                    "//div[@class='book-content']",
                    "//div[@class='text']",
                    "//div[@class='entry-content']",
                    "//div[contains(@class, 'chapter-content')]",
                    "//div[@id='booktxt']",
                    "//div[contains(@class, 'book_text')]",
                    "//div[contains(@class, 'reader-content')]",
                    "//div[@class='entry']",
                    "//article"
                };

                HtmlNode contentNode = null;
                foreach (var selector in contentSelectors)
                {
                    contentNode = doc.DocumentNode.SelectSingleNode(selector);
                    if (contentNode != null) break;
                }

                if (contentNode == null)
                {
                    var paragraphs = doc.DocumentNode.SelectNodes("//div[@class='entry']//p | //article//p | //div[@class='text']//p");
                    if (paragraphs != null && paragraphs.Count > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var p in paragraphs)
                        {
                            var text = WebUtility.HtmlDecode(Regex.Replace(p.InnerText.Trim(), @"\s+", " "));
                            if (!string.IsNullOrEmpty(text))
                                sb.AppendLine(text);
                        }
                        return sb.ToString();
                    }
                    return "Текст книги не найден";
                }

                RemoveUnwantedElements(contentNode);
                return CleanHtmlContent(contentNode.InnerHtml);
            }
            catch (Exception ex)
            {
                return $"Ошибка загрузки текста: {ex.Message}";
            }
        }

        private int ExtractReadliBookId(string url)
        {
            if (string.IsNullOrEmpty(url)) return 0;

            var patterns = new[]
            {
                @"[?&]b=(\d+)",
                @"/book/(\d+)",
                @"-(\d+)/$",
                @"/(\d+)/?$"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(url, pattern, RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
                    return id;
            }

            return 0;
        }
    }
}