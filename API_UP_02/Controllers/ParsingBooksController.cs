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

        [HttpGet("books/many")]
        public IActionResult GetManyBooks([FromQuery] int count = 20)
        {
            try
            {
                var books = ParseManyLitmirBooks(count);

                foreach (var book in books.Where(b => b.Id > 0))
                {
                    book.Content = ParseLitmirBookContentFirstPages(book.Id, 5);
                    Console.WriteLine($"Загружено {book.Content?.Length ?? 0} символов контента для книги {book.Title}");
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

        [HttpGet("books/pages")]
        public IActionResult GetBooksFromPages([FromQuery] int pages = 2)
        {
            try
            {
                var books = ParseLitmirBooksFromMultiplePages(pages);

                foreach (var book in books.Where(b => b.Id > 0))
                {
                    book.Content = ParseLitmirBookContentFirstPages(book.Id, 5);
                    Console.WriteLine($"Загружено {book.Content?.Length ?? 0} символов контента для книги {book.Title}");
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

        [HttpGet("book/content")]
        public IActionResult GetBookContent([FromQuery] string bookUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(bookUrl))
                    return BadRequest("Не указан URL книги");

                var bookId = ExtractLitmirBookId(bookUrl);
                var content = ParseLitmirBookContentFirstPages(bookId, 5);
                return Ok(new { Url = bookUrl, Content = content });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new { Error = ex.Message, Content = "" });
            }
        }

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

        [HttpGet("book/{bookId}/content")]
        public IActionResult GetBookContentById(int bookId)
        {
            try
            {
                if (bookId <= 0)
                    return BadRequest("Некорректный ID книги");

                var content = ParseLitmirBookContentFirstPages(bookId, 5);

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

        [HttpGet("book/{bookId}/read")]
        public IActionResult ReadBook(int bookId, [FromQuery] int? page = null)
        {
            try
            {
                if (bookId <= 0)
                    return BadRequest("Некорректный ID книги");

                var (content, totalPages) = ParseLitmirBookPage(bookId, page ?? 1);
                var navigation = GetLitmirBookNavigation(bookId, page ?? 1, totalPages);

                return Ok(new
                {
                    BookId = bookId,
                    Page = page ?? 1,
                    TotalPages = totalPages,
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

        [HttpGet("books/genre/{genre}")]
        public IActionResult GetBooksByGenre(string genre, [FromQuery] int count = 10)
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

        [HttpGet("books/author/{author}")]
        public IActionResult GetBooksByAuthor(string author, [FromQuery] int count = 10)
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

        private string ParseLitmirBookContentFirstPages(int bookId, int pagesToLoad = 5)
        {
            var web = CreateHtmlWeb();
            var fullContent = new StringBuilder();

            try
            {
                Console.WriteLine($"Начинаем загрузку первых {pagesToLoad} страниц для книги {bookId}");

                for (int currentPage = 1; currentPage <= pagesToLoad; currentPage++)
                {
                    string pageUrl = $"{LitmirBaseUrl}/br/?b={bookId}&p={currentPage}";

                    try
                    {
                        Console.WriteLine($"Загрузка страницы {currentPage} для книги {bookId}");
                        var doc = web.Load(pageUrl);

                        var textContainer = doc.DocumentNode.SelectSingleNode("//div[@class='page_text']") ??
                                           doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'page_text')]") ??
                                           doc.DocumentNode.SelectSingleNode("//div[@id='book_text']") ??
                                           doc.DocumentNode.SelectSingleNode("//div[@class='text']");

                        if (textContainer == null)
                        {
                            Console.WriteLine($"Текст не найден на странице {currentPage}");
                            if (currentPage == 1)
                                return "Текст книги не найден";
                            break;
                        }

                        RemoveUnwantedElements(textContainer);

                        string pageContent = CleanHtmlContent(textContainer.InnerHtml);

                        if (string.IsNullOrWhiteSpace(pageContent))
                        {
                            Console.WriteLine($"Пустая страница {currentPage}");
                            continue;
                        }

                        if (currentPage > 1)
                        {
                            fullContent.AppendLine();
                            fullContent.AppendLine();
                            fullContent.AppendLine($"=== Страница {currentPage} ===");
                            fullContent.AppendLine();
                        }

                        fullContent.Append(pageContent);
                        Console.WriteLine($"Страница {currentPage} загружена, длина: {pageContent.Length} символов");

                        System.Threading.Thread.Sleep(300);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка на странице {currentPage}: {ex.Message}");
                    }
                }

                string result = fullContent.ToString();
                Console.WriteLine($"Всего загружено страниц: {pagesToLoad}, общая длина контента: {result.Length} символов для книги {bookId}");

                return string.IsNullOrEmpty(result) ? "Контент не найден" : result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при загрузке книги {bookId}: {ex.Message}");
                return $"Ошибка загрузки текста: {ex.Message}";
            }
        }

        private string ParseLitmirBookContentFull(int bookId)
        {
            var web = CreateHtmlWeb();
            var fullContent = new StringBuilder();
            int currentPage = 1;
            int maxPages = 1000;
            bool hasMorePages = true;

            try
            {
                while (hasMorePages && currentPage <= maxPages)
                {
                    string pageUrl = $"{LitmirBaseUrl}/br/?b={bookId}&p={currentPage}";

                    try
                    {
                        Console.WriteLine($"Загрузка страницы {currentPage} для книги {bookId}");
                        var doc = web.Load(pageUrl);

                        var textContainer = doc.DocumentNode.SelectSingleNode("//div[@class='page_text']") ??
                                           doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'page_text')]") ??
                                           doc.DocumentNode.SelectSingleNode("//div[@id='book_text']") ??
                                           doc.DocumentNode.SelectSingleNode("//div[@class='text']");

                        if (textContainer == null)
                        {
                            Console.WriteLine($"Текст не найден на странице {currentPage}");
                            if (currentPage == 1)
                                return "Текст книги не найден";
                            break;
                        }

                        RemoveUnwantedElements(textContainer);

                        string pageContent = CleanHtmlContent(textContainer.InnerHtml);

                        if (string.IsNullOrWhiteSpace(pageContent))
                        {
                            Console.WriteLine($"Пустая страница {currentPage}");
                            if (currentPage > 1)
                                break;
                        }

                        if (currentPage > 1)
                        {
                            fullContent.AppendLine();
                            fullContent.AppendLine();
                            fullContent.AppendLine($"=== Страница {currentPage} ===");
                            fullContent.AppendLine();
                        }

                        fullContent.Append(pageContent);

                        var nextPageLink = doc.DocumentNode.SelectSingleNode("//a[contains(text(), 'Следующая') or contains(text(), '→')]");
                        if (nextPageLink == null)
                        {
                            hasMorePages = false;
                            Console.WriteLine($"Достигнут конец книги на странице {currentPage}");
                        }

                        currentPage++;

                        if (currentPage % 10 == 0)
                        {
                            Console.WriteLine($"Загружено {currentPage - 1} страниц для книги {bookId}");
                        }

                        System.Threading.Thread.Sleep(300);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка на странице {currentPage}: {ex.Message}");
                        if (currentPage > 1)
                            break;
                        return $"Ошибка загрузки текста: {ex.Message}";
                    }
                }

                Console.WriteLine($"Всего загружено страниц: {currentPage - 1} для книги {bookId}");
                return fullContent.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при загрузке книги {bookId}: {ex.Message}");
                return $"Ошибка загрузки текста: {ex.Message}";
            }
        }

        private (string Content, int TotalPages) ParseLitmirBookPage(int bookId, int page)
        {
            var web = CreateHtmlWeb();

            try
            {
                string pageUrl = $"{LitmirBaseUrl}/br/?b={bookId}&p={page}";
                var doc = web.Load(pageUrl);

                var textContainer = doc.DocumentNode.SelectSingleNode("//div[@class='page_text']") ??
                                   doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'page_text')]") ??
                                   doc.DocumentNode.SelectSingleNode("//div[@id='book_text']") ??
                                   doc.DocumentNode.SelectSingleNode("//div[@class='text']");

                if (textContainer == null)
                    return ("Текст не найден", 0);

                RemoveUnwantedElements(textContainer);
                string content = CleanHtmlContent(textContainer.InnerHtml);

                int totalPages = 1;
                var lastPageLink = doc.DocumentNode.SelectNodes("//a[contains(@href, '&p=')]");
                if (lastPageLink != null && lastPageLink.Count > 0)
                {
                    var pageNumbers = new List<int>();
                    foreach (var link in lastPageLink)
                    {
                        var href = link.GetAttributeValue("href", "");
                        var match = Regex.Match(href, @"[?&]p=(\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int p))
                            pageNumbers.Add(p);
                    }
                    if (pageNumbers.Any())
                        totalPages = pageNumbers.Max();
                }

                return (content, totalPages);
            }
            catch (Exception ex)
            {
                return ($"Ошибка загрузки текста: {ex.Message}", 0);
            }
        }

        private object GetLitmirBookNavigation(int bookId, int currentPage, int totalPages)
        {
            return new
            {
                CurrentPage = currentPage,
                TotalPages = totalPages,
                HasPrevious = currentPage > 1,
                HasNext = currentPage < totalPages,
                PreviousPage = currentPage > 1 ? currentPage - 1 : (int?)null,
                NextPage = currentPage < totalPages ? currentPage + 1 : (int?)null,
                FirstPage = 1,
                LastPage = totalPages
            };
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

                        if (!string.IsNullOrEmpty(book.Content) && book.Content.Length > 50000)
                        {
                            book.Content = book.Content.Substring(0, 50000) + "... (обрезано)";
                        }

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
                            string contentToSave = book.Content;
                            if (contentToSave.Length > 50000)
                            {
                                contentToSave = contentToSave.Substring(0, 50000) + "... (обрезано)";
                            }
                            existingBook.Content = contentToSave;
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

            while (allBooks.Count < targetCount && currentPage <= 3)
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
                    if (allBooks.Count >= 20)
                        break;

                    var book = ParseLitmirBook(row);
                    if (book != null && !string.IsNullOrEmpty(book.Title) && book.Id > 0)
                    {
                        allBooks.Add(book);
                    }
                }

                if (allBooks.Count >= 20)
                    break;
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
                book.Year = ParseLitmirYear(row);
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

                book.Content = ParseLitmirBookContentFirstPages(bookId, 5);

                return book;
            }
            catch (Exception ex)
            {
                return new Book { Id = 0, Title = "Ошибка", Description = ex.Message, BookUrl = bookUrl };
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

        private int? ParseLitmirYear(HtmlNode row)
        {
            var text = row.InnerText;
            var match = Regex.Match(text, @"(?:Год[:\s]*|\()(\d{4})\)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int year))
                return year;
            return null;
        }
    }
}