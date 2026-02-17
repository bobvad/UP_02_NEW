using API_UP_02.Models;
using API_UP_02.Context;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.RegularExpressions;

namespace API_UP_02.Controllers
{
    /// <summary>
    /// Контроллер для парсинга книг с сайтов Litmir.club и Author.Today
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "v3")]
    public class ParsingBooksController : ControllerBase
    {
        private const string LitmirBaseUrl = "https://litmir.club";
        private const string AuthorTodayBaseUrl = "https://author.today";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

        private readonly BooksContext _context;

        /// <summary>
        /// Конструктор контроллера парсинга книг
        /// </summary>
        /// <param name="context">Контекст базы данных книг</param>
        public ParsingBooksController(BooksContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Получает книги с одной страницы Litmir.club и сохраняет в базу данных
        /// </summary>
        /// <returns>Список спарсенных книг</returns>
        [HttpGet("books/single-page")]
        public IActionResult GetBooksFromSinglePage()
        {
            try
            {
                var books = ParseLitmirBooksFromSinglePage();
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
        /// Получает указанное количество книг с Litmir.club и сохраняет в базу данных
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
        /// Получает книги с нескольких страниц Litmir.club и сохраняет в базу данных
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
        /// Получает книги с сайта Author.Today и сохраняет в базу данных
        /// </summary>
        /// <param name="count">Количество книг для парсинга (по умолчанию 50)</param>
        /// <returns>Список спарсенных книг</returns>
        [HttpGet("authortoday/books")]
        public async Task<IActionResult> GetAuthorTodayBooks([FromQuery] int count = 50)
        {
            try
            {
                var books = await ParseAuthorTodayBooksAsync(count);

                foreach (var book in books.Where(b => !string.IsNullOrEmpty(b.ReadUrl)))
                {
                    var bookId = ExtractAuthorTodayId(book.ReadUrl);
                    if (!string.IsNullOrEmpty(bookId))
                    {
                        book.Content = await ParseAuthorTodayBookContentAsync(bookId);
                    }
                }

                var validBooks = books.Where(b => !string.IsNullOrEmpty(b.Title)).ToList();

                if (validBooks.Any())
                {
                    SaveBooksToDatabase(validBooks);
                    return Ok(validBooks);
                }

                return Ok(new List<Book>());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new List<Book>());
            }
        }

        /// <summary>
        /// Получает текст книги с Author.Today по ID
        /// </summary>
        /// <param name="bookId">ID книги на Author.Today</param>
        /// <returns>Текст книги</returns>
        [HttpGet("authortoday/book/{bookId}/content")]
        public async Task<IActionResult> GetAuthorTodayBookContent(string bookId)
        {
            try
            {
                if (string.IsNullOrEmpty(bookId))
                    return BadRequest("Не указан ID книги");

                var content = await ParseAuthorTodayBookContentAsync(bookId);

                var book = await _context.Books.FirstOrDefaultAsync(b => b.ReadUrl.Contains(bookId));
                if (book != null)
                {
                    book.Content = TruncateString(content, 5000);
                    await _context.SaveChangesAsync();
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
        /// Диагностика Author.Today - проверяет, что возвращает сайт
        /// </summary>
        /// <returns>Результаты диагностики</returns>
        [HttpGet("authortoday/debug")]
        public async Task<IActionResult> DebugAuthorToday()
        {
            var result = new Dictionary<string, object>();

            try
            {
                using (var handler = new HttpClientHandler())
                {
                    handler.UseCookies = true;
                    handler.CookieContainer = new CookieContainer();
                    handler.AllowAutoRedirect = true;
                    handler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;

                    using (var client = new HttpClient(handler))
                    {
                        var userAgents = new[]
                        {
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/121.0"
                        };

                        var url = "https://author.today/work/popular";

                        foreach (var ua in userAgents)
                        {
                            client.DefaultRequestHeaders.Clear();
                            client.DefaultRequestHeaders.Add("User-Agent", ua);
                            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                            client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
                            client.DefaultRequestHeaders.Add("Referer", "https://author.today/");

                            var response = await client.GetAsync(url);
                            var content = await response.Content.ReadAsStringAsync();

                            result[$"UA_{ua.Substring(0, 30)}..."] = new
                            {
                                StatusCode = (int)response.StatusCode,
                                ContentLength = content.Length,
                                ContentPreview = content.Length > 500 ? content.Substring(0, 500) + "..." : content,
                                HasBooks = content.Contains("book") || content.Contains("книг") || content.Contains("Work")
                            };
                        }
                    }
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Получает книги со всех источников (Litmir.club и Author.Today) и сохраняет в базу данных
        /// </summary>
        /// <param name="count">Общее количество книг для парсинга (по умолчанию 100)</param>
        /// <returns>Список спарсенных книг</returns>
        [HttpGet("all/books")]
        public async Task<IActionResult> GetAllBooks([FromQuery] int count = 100)
        {
            try
            {
                var allBooks = new List<Book>();

                var litmirBooks = ParseManyLitmirBooks(count / 2);
                foreach (var book in litmirBooks.Where(b => b.Id > 0))
                {
                    book.Content = ParseLitmirBookContent(book.ReadUrl);
                }
                allBooks.AddRange(litmirBooks.Where(b => b.Id > 0));

                var authorTodayBooks = await ParseAuthorTodayBooksAsync(count / 2);

                foreach (var book in authorTodayBooks.Where(b => !string.IsNullOrEmpty(b.ReadUrl)))
                {
                    var bookId = ExtractAuthorTodayId(book.ReadUrl);
                    if (!string.IsNullOrEmpty(bookId))
                    {
                        book.Content = await ParseAuthorTodayBookContentAsync(bookId);
                    }
                }
                allBooks.AddRange(authorTodayBooks.Where(b => !string.IsNullOrEmpty(b.Title)));

                SaveBooksToDatabase(allBooks);
                return Ok(allBooks);
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
                    book.Content = TruncateString(content, 5000);
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
        /// Сохраняет список книг в базу данных
        /// </summary>
        /// <param name="books">Список книг для сохранения</param>
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
                        book.Content = TruncateString(book.Content, 5000);

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

        /// <summary>
        /// Обрезает строку до указанной длины
        /// </summary>
        /// <param name="value">Исходная строка</param>
        /// <param name="maxLength">Максимальная длина</param>
        /// <returns>Обрезанная строка</returns>
        private string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        }

        /// <summary>
        /// Извлекает ID книги из URL Author.Today
        /// </summary>
        /// <param name="url">URL книги</param>
        /// <returns>ID книги или null</returns>
        private string ExtractAuthorTodayId(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var match = Regex.Match(url, @"/work/([^/]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Парсит книги с одной страницы Litmir.club
        /// </summary>
        /// <returns>Список книг</returns>
        private List<Book> ParseLitmirBooksFromSinglePage()
        {
            var books = new List<Book>();
            var web = CreateHtmlWeb();

            try
            {
                var doc = web.Load(LitmirBaseUrl);
                var bookBlocks = FindLitmirBookBlocks(doc);

                if (bookBlocks == null || bookBlocks.Count == 0)
                {
                    Console.WriteLine("Книги не найдены на странице");
                    return books;
                }

                foreach (var block in bookBlocks)
                {
                    var book = ParseLitmirBook(block);
                    if (book != null && !string.IsNullOrEmpty(book.Title) && book.Id > 0)
                    {
                        books.Add(book);
                    }
                }

                Console.WriteLine($"Спарсено {books.Count} книг с корректным ID");
                return books;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return books;
            }
        }

        /// <summary>
        /// Парсит указанное количество книг с Litmir.club
        /// </summary>
        /// <param name="targetCount">Целевое количество книг</param>
        /// <returns>Список книг</returns>
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

        /// <summary>
        /// Парсит книги с нескольких страниц Litmir.club
        /// </summary>
        /// <param name="pagesCount">Количество страниц</param>
        /// <returns>Список книг</returns>
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

        /// <summary>
        /// Парсит книги с сайта Author.Today
        /// </summary>
        /// <param name="count">Количество книг</param>
        /// <returns>Список книг</returns>
        private async Task<List<Book>> ParseAuthorTodayBooksAsync(int count)
        {
            var books = new List<Book>();

            try
            {
                using (var handler = new HttpClientHandler())
                {
                    handler.UseCookies = true;
                    handler.CookieContainer = new CookieContainer();
                    handler.AllowAutoRedirect = true;
                    handler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;

                    using (var client = new HttpClient(handler))
                    {
                        client.DefaultRequestHeaders.Clear();
                        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
                        client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
                        client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
                        client.DefaultRequestHeaders.Add("Referer", "https://author.today/");
                        client.DefaultRequestHeaders.Add("Connection", "keep-alive");

                        var mainResponse = await client.GetAsync("https://author.today/");
                        if (!mainResponse.IsSuccessStatusCode)
                        {
                            Console.WriteLine("Не удалось получить главную страницу Author.Today");
                            return books;
                        }

                        await Task.Delay(2000);

                        var response = await client.GetAsync("https://author.today/work/popular");
                        if (response.IsSuccessStatusCode)
                        {
                            var html = await response.Content.ReadAsStringAsync();
                            var doc = new HtmlDocument();
                            doc.LoadHtml(html);
                            books = ParseAuthorTodayFromHtml(doc, count);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка парсинга Author.Today: {ex.Message}");
            }

            return books;
        }

        /// <summary>
        /// Парсит текст книги с Author.Today по ID
        /// </summary>
        /// <param name="bookId">ID книги</param>
        /// <returns>Текст книги</returns>
        private async Task<string> ParseAuthorTodayBookContentAsync(string bookId)
        {
            try
            {
                using (var handler = new HttpClientHandler())
                {
                    handler.UseCookies = true;
                    handler.CookieContainer = new CookieContainer();
                    handler.AllowAutoRedirect = true;

                    using (var client = new HttpClient(handler))
                    {
                        client.DefaultRequestHeaders.Clear();
                        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                        client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
                        client.DefaultRequestHeaders.Add("Referer", "https://author.today/");

                        var url = $"{AuthorTodayBaseUrl}/work/{bookId}/read";
                        var response = await client.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            var html = await response.Content.ReadAsStringAsync();
                            var doc = new HtmlDocument();
                            doc.LoadHtml(html);

                            var contentNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'book-content')]") ??
                                             doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'chapter-content')]") ??
                                             doc.DocumentNode.SelectSingleNode("//div[@class='text']") ??
                                             doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'content')]");

                            if (contentNode != null)
                            {
                                RemoveUnwantedElements(contentNode);
                                return CleanHtmlContent(contentNode.InnerHtml);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки текста Author.Today: {ex.Message}");
            }

            return "Не удалось загрузить текст книги";
        }

        /// <summary>
        /// Парсит HTML страницы Author.Today
        /// </summary>
        /// <param name="doc">HTML документ</param>
        /// <param name="count">Количество книг</param>
        /// <returns>Список книг</returns>
        private List<Book> ParseAuthorTodayFromHtml(HtmlDocument doc, int count)
        {
            var books = new List<Book>();

            try
            {
                var selectors = new[]
                {
                    "//div[contains(@class, 'book-row')]",
                    "//div[contains(@class, 'BookRow')]",
                    "//article[contains(@class, 'book')]",
                    "//div[contains(@class, 'work-item')]",
                    "//div[@data-work-id]",
                    "//a[contains(@href, '/work/')]/parent::div"
                };

                HtmlNodeCollection nodes = null;

                foreach (var selector in selectors)
                {
                    nodes = doc.DocumentNode.SelectNodes(selector);
                    if (nodes != null && nodes.Count > 0)
                    {
                        break;
                    }
                }

                if (nodes != null)
                {
                    foreach (var node in nodes.Take(count))
                    {
                        var book = ParseAuthorTodayBook(node);
                        if (book != null && !string.IsNullOrEmpty(book.Title))
                        {
                            books.Add(book);
                        }
                    }
                }

                if (!books.Any())
                {
                    var links = doc.DocumentNode.SelectNodes("//a[contains(@href, '/work/')]");
                    if (links != null)
                    {
                        var processedUrls = new HashSet<string>();

                        foreach (var link in links.Take(count * 2))
                        {
                            var href = link.GetAttributeValue("href", "");
                            if (string.IsNullOrEmpty(href) || !href.Contains("/work/")) continue;

                            var fullUrl = BuildFullUrl(href, AuthorTodayBaseUrl);
                            if (processedUrls.Contains(fullUrl)) continue;

                            processedUrls.Add(fullUrl);

                            var title = WebUtility.HtmlDecode(link.InnerText.Trim());
                            if (string.IsNullOrEmpty(title) || title.Length < 3) continue;

                            var idMatch = Regex.Match(fullUrl, @"/work/([^/]+)");
                            if (idMatch.Success)
                            {
                                var book = new Book
                                {
                                    Title = title,
                                    BookUrl = fullUrl,
                                    ReadUrl = $"{AuthorTodayBaseUrl}/work/{idMatch.Groups[1].Value}/read",
                                    Author = "Неизвестен",
                                    Description = "Описание отсутствует",
                                    Genre = "Жанр не указан",
                                    Language = "Русский"
                                };

                                books.Add(book);

                                if (books.Count >= count)
                                    break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка парсинга HTML: {ex.Message}");
            }

            return books;
        }

        /// <summary>
        /// Парсит отдельную книгу с Litmir.club из HTML-узла
        /// </summary>
        /// <param name="row">HTML-узел с данными книги</param>
        /// <returns>Объект книги или null в случае ошибки</returns>
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

        /// <summary>
        /// Парсит отдельную книгу с Author.Today из HTML-узла
        /// </summary>
        /// <param name="node">HTML-узел с данными книги</param>
        /// <returns>Объект книги или null в случае ошибки</returns>
        private Book ParseAuthorTodayBook(HtmlNode node)
        {
            try
            {
                var book = new Book
                {
                    Language = "Русский"
                };

                var titleNode = node.SelectSingleNode(".//a[contains(@class, 'book-title')]") ??
                               node.SelectSingleNode(".//h2/a") ??
                               node.SelectSingleNode(".//a[contains(@href, '/work/')]");

                if (titleNode != null)
                {
                    book.Title = WebUtility.HtmlDecode(titleNode.InnerText.Trim());
                    var href = titleNode.GetAttributeValue("href", "");
                    book.BookUrl = BuildFullUrl(href, AuthorTodayBaseUrl);

                    var idMatch = Regex.Match(book.BookUrl, @"/work/([^/]+)");
                    if (idMatch.Success)
                    {
                        var id = idMatch.Groups[1].Value;
                        book.ReadUrl = $"{AuthorTodayBaseUrl}/work/{id}/read";
                    }
                }

                var authorNode = node.SelectSingleNode(".//a[contains(@class, 'author-name')]") ??
                                node.SelectSingleNode(".//span[contains(@class, 'author')]/a");

                book.Author = authorNode != null
                    ? WebUtility.HtmlDecode(authorNode.InnerText.Trim())
                    : "Неизвестен";

                var descNode = node.SelectSingleNode(".//div[contains(@class, 'description')]") ??
                              node.SelectSingleNode(".//p[contains(@class, 'annotation')]");

                book.Description = descNode != null
                    ? WebUtility.HtmlDecode(Regex.Replace(descNode.InnerText.Trim(), @"\s+", " "))
                    : "Описание отсутствует";

                var genreNodes = node.SelectNodes(".//a[contains(@href, '/genre/')]");
                if (genreNodes != null)
                {
                    var genres = genreNodes
                        .Select(g => WebUtility.HtmlDecode(g.InnerText.Trim()))
                        .Where(g => !string.IsNullOrEmpty(g))
                        .ToList();
                    book.Genre = genres.Any() ? string.Join(", ", genres) : "Жанр не указан";
                }

                var imgNode = node.SelectSingleNode(".//img[contains(@class, 'cover')]") ??
                             node.SelectSingleNode(".//img");

                if (imgNode != null)
                {
                    var src = imgNode.GetAttributeValue("src", "") ??
                             imgNode.GetAttributeValue("data-src", "");
                    book.ImageUrl = BuildFullUrl(src, AuthorTodayBaseUrl);
                }

                return book;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Находит ссылку на книгу в HTML-узле Litmir.club
        /// </summary>
        /// <param name="row">HTML-узел</param>
        /// <returns>Узел ссылки или null</returns>
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

        /// <summary>
        /// Парсит автора книги с Litmir.club
        /// </summary>
        /// <param name="row">HTML-узел</param>
        /// <returns>Имя автора</returns>
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

        /// <summary>
        /// Парсит URL изображения книги с Litmir.club
        /// </summary>
        /// <param name="row">HTML-узел</param>
        /// <returns>URL изображения</returns>
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

        /// <summary>
        /// Парсит описание книги с Litmir.club
        /// </summary>
        /// <param name="row">HTML-узел</param>
        /// <returns>Описание книги</returns>
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

        /// <summary>
        /// Парсит жанры книги с Litmir.club
        /// </summary>
        /// <param name="row">HTML-узел</param>
        /// <returns>Строка с жанрами</returns>
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

        /// <summary>
        /// Парсит количество страниц книги с Litmir.club
        /// </summary>
        /// <param name="row">HTML-узел</param>
        /// <returns>Количество страниц или null</returns>
        private int? ParseLitmirPageCount(HtmlNode row)
        {
            var match = Regex.Match(row.InnerText, @"Страниц:\s*(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int pages))
                return pages;

            return null;
        }

        /// <summary>
        /// Проверяет, завершена ли книга на Litmir.club
        /// </summary>
        /// <param name="row">HTML-узел</param>
        /// <returns>true если книга завершена</returns>
        private bool CheckLitmirIsCompleted(HtmlNode row)
        {
            var text = row.InnerText;
            return text.Contains("Книга закончена") ||
                   text.Contains("Завершено") ||
                   text.Contains("Полный текст");
        }

        /// <summary>
        /// Парсит текст книги с Litmir.club по URL
        /// </summary>
        /// <param name="bookUrl">URL книги</param>
        /// <returns>Текст книги</returns>
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

        /// <summary>
        /// Парсит полную информацию о книге с Litmir.club
        /// </summary>
        /// <param name="bookUrl">URL книги</param>
        /// <returns>Объект книги с полной информацией</returns>
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

        /// <summary>
        /// Получает навигацию по страницам книги на Litmir.club
        /// </summary>
        /// <param name="bookUrl">URL книги</param>
        /// <returns>Объект с ссылками на предыдущую и следующую страницы</returns>
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

        /// <summary>
        /// Извлекает ID книги из URL Litmir.club
        /// </summary>
        /// <param name="url">URL книги</param>
        /// <returns>ID книги или 0</returns>
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

        /// <summary>
        /// Находит блоки с книгами на странице Litmir.club
        /// </summary>
        /// <param name="doc">HTML-документ</param>
        /// <returns>Коллекция узлов с книгами</returns>
        private HtmlNodeCollection FindLitmirBookBlocks(HtmlDocument doc)
        {
            return doc.DocumentNode.SelectNodes("//div[@class='book_block']") ??
                   doc.DocumentNode.SelectNodes("//div[contains(@class, 'book-item')]") ??
                   doc.DocumentNode.SelectNodes("//tr[.//span[@itemprop='name']]");
        }

        /// <summary>
        /// Находит строки с книгами на странице Litmir.club
        /// </summary>
        /// <param name="doc">HTML-документ</param>
        /// <returns>Коллекция узлов с книгами</returns>
        private HtmlNodeCollection FindLitmirBookRows(HtmlDocument doc)
        {
            return doc.DocumentNode.SelectNodes("//table[@class='']//tr") ??
                   doc.DocumentNode.SelectNodes("//tr[.//span[@itemprop='name']]") ??
                   doc.DocumentNode.SelectNodes("//div[contains(@class, 'book-item')]") ??
                   doc.DocumentNode.SelectNodes("//div[@class='book_block']");
        }

        /// <summary>
        /// Создает веб-клиент для парсинга
        /// </summary>
        /// <returns>Настроенный HtmlWeb</returns>
        private HtmlWeb CreateHtmlWeb()
        {
            return new HtmlWeb { UserAgent = UserAgent };
        }

        /// <summary>
        /// Создает веб-клиент для парсинга Author.Today с дополнительными заголовками
        /// </summary>
        /// <returns>Настроенный HtmlWeb</returns>
        private HtmlWeb CreateAuthorTodayWebClient()
        {
            var web = new HtmlWeb { UserAgent = UserAgent };
            web.PreRequest = request =>
            {
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.Add("Accept-Language", "ru-RU,ru;q=0.8,en-US;q=0.5,en;q=0.3");
                return true;
            };
            return web;
        }

        /// <summary>
        /// Формирует полный URL на основе относительного и базового
        /// </summary>
        /// <param name="url">Относительный или абсолютный URL</param>
        /// <param name="baseUrl">Базовый URL сайта</param>
        /// <returns>Полный URL</returns>
        private string BuildFullUrl(string url, string baseUrl)
        {
            if (string.IsNullOrEmpty(url)) return url;

            if (url.StartsWith("//")) return "https:" + url;
            if (url.StartsWith("/")) return baseUrl + url;
            if (!url.StartsWith("http")) return baseUrl + "/" + url;

            return url;
        }

        /// <summary>
        /// Удаляет нежелательные элементы из HTML-контейнера
        /// </summary>
        /// <param name="container">HTML-контейнер</param>
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

        /// <summary>
        /// Очищает HTML-контент от тегов и лишних пробелов
        /// </summary>
        /// <param name="html">Исходный HTML</param>
        /// <returns>Очищенный текст</returns>
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
    }
}