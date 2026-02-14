using API_UP_02.Models;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.RegularExpressions;

namespace API_UP_02.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "v3")]
    public class ParsingBooksController : ControllerBase
    {
        private const string BaseUrl = "https://litmir.club";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

        /// <summary>
        /// Получить книги с одной страницы
        /// </summary>
        [HttpGet("books/single-page")]
        public IActionResult GetBooksFromSinglePage()
        {
            try
            {
                var books = ParseBooksFromSinglePage();
                var validBooks = books.Where(b => b.Id > 0).ToList();
                return Ok(validBooks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new List<Book>());
            }
        }

        /// <summary>
        /// Получить много книг с сайта
        /// </summary>
        [HttpGet("books/many")]
        public IActionResult GetManyBooks([FromQuery] int count = 50)
        {
            try
            {
                var books = ParseManyBooksFromLitmir(count);
                // Фильтруем книги с ID > 0
                var validBooks = books.Where(b => b.Id > 0).ToList();
                return Ok(validBooks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new List<Book>());
            }
        }

        /// <summary>
        /// Получить книги с нескольких страниц
        /// </summary>
        [HttpGet("books/pages")]
        public IActionResult GetBooksFromPages([FromQuery] int pages = 3)
        {
            try
            {
                var books = ParseBooksFromMultiplePages(pages);
                var validBooks = books.Where(b => b.Id > 0).ToList();
                return Ok(validBooks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new List<Book>());
            }
        }

        /// <summary>
        /// Получить текст книги по URL
        /// </summary>
        [HttpGet("book/content")]
        public IActionResult GetBookContent([FromQuery] string bookUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(bookUrl))
                    return BadRequest("Не указан URL книги");

                var content = ParseBookContent(bookUrl);
                return Ok(new { Url = bookUrl, Content = content });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new { Error = ex.Message, Content = "" });
            }
        }

        /// <summary>
        /// Получить полную информацию о книге 
        /// </summary>
        [HttpGet("book/full")]
        public IActionResult GetFullBookInfo([FromQuery] string bookUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(bookUrl))
                    return BadRequest("Не указан URL книги");

                var book = ParseFullBookInfo(bookUrl);

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
        /// Получить текст книги по ID 
        /// </summary>
        [HttpGet("book/{bookId}/content")]
        public IActionResult GetBookContentById(int bookId)
        {
            try
            {
                if (bookId <= 0)
                    return BadRequest("Некорректный ID книги");

                var bookUrl = $"{BaseUrl}/br/?b={bookId}";
                var content = ParseBookContent(bookUrl);
                return Ok(new { BookId = bookId, Content = content });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new { Error = ex.Message, Content = "" });
            }
        }

        /// <summary>
        /// Читать книгу постранично 
        /// </summary>
        [HttpGet("book/{bookId}/read")]
        public IActionResult ReadBook(int bookId, [FromQuery] int? page = null)
        {
            try
            {
                if (bookId <= 0)
                    return BadRequest("Некорректный ID книги");

                var bookUrl = page.HasValue
                    ? $"{BaseUrl}/br/?b={bookId}&p={page.Value}"
                    : $"{BaseUrl}/br/?b={bookId}";

                var content = ParseBookContent(bookUrl);
                var navigation = GetBookNavigation(bookUrl);

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
        /// Получить книги по жанру 
        /// </summary>
        [HttpGet("books/genre/{genre}")]
        public IActionResult GetBooksByGenre(string genre, [FromQuery] int count = 20)
        {
            try
            {
                var allBooks = ParseManyBooksFromLitmir(count * 2); 
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
        /// Получить книги по автору 
        /// </summary>
        [HttpGet("books/author/{author}")]
        public IActionResult GetBooksByAuthor(string author, [FromQuery] int count = 20)
        {
            try
            {
                var allBooks = ParseManyBooksFromLitmir(count * 2); // Парсим с запасом
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

        private List<Book> ParseBooksFromSinglePage()
        {
            var books = new List<Book>();
            var web = CreateHtmlWeb();

            try
            {
                var doc = web.Load(BaseUrl);
                var bookBlocks = FindBookBlocks(doc);

                if (bookBlocks == null || bookBlocks.Count == 0)
                {
                    Console.WriteLine("Книги не найдены на странице");
                    return books;
                }

                foreach (var block in bookBlocks)
                {
                    var book = ParseBook(block);
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

        private List<Book> ParseManyBooksFromLitmir(int targetCount)
        {
            var allBooks = new List<Book>();
            var web = CreateHtmlWeb();
            var currentPage = 1;

            while (allBooks.Count < targetCount && currentPage <= 10)
            {
                var pageUrl = currentPage == 1 ? BaseUrl : $"{BaseUrl}/knigi?page={currentPage}";
                var doc = web.Load(pageUrl);
                var bookRows = FindBookRows(doc);

                if (bookRows == null || bookRows.Count == 0)
                    break;

                foreach (var row in bookRows)
                {
                    if (allBooks.Count >= targetCount)
                        break;

                    var book = ParseBook(row);
                    if (book != null && !string.IsNullOrEmpty(book.Title) && book.Id > 0)
                    {
                        allBooks.Add(book);
                    }
                }

                currentPage++;
            }

            return allBooks;
        }

        private List<Book> ParseBooksFromMultiplePages(int pagesCount)
        {
            var allBooks = new List<Book>();
            var web = CreateHtmlWeb();

            for (int pageNum = 1; pageNum <= pagesCount; pageNum++)
            {
                var pageUrl = pageNum == 1 ? BaseUrl : $"{BaseUrl}/knigi?page={pageNum}";
                var doc = web.Load(pageUrl);
                var bookRows = FindBookRows(doc);

                if (bookRows == null || bookRows.Count == 0)
                    continue;

                foreach (var row in bookRows)
                {
                    var book = ParseBook(row);
                    if (book != null && !string.IsNullOrEmpty(book.Title) && book.Id > 0)
                    {
                        allBooks.Add(book);
                    }
                }
            }

            return allBooks;
        }

        private Book ParseBook(HtmlNode row)
        {
            try
            {
                var book = new Book();

                var linkNode = FindBookLink(row);
                if (linkNode == null) return null;

                book.Title = WebUtility.HtmlDecode(linkNode.InnerText.Trim());

                var href = linkNode.GetAttributeValue("href", "");
                book.BookUrl = BuildFullUrl(href);

                book.Id = ExtractBookId(book.BookUrl);

                if (book.Id == 0) return null;

                if (book.Id > 0)
                {
                    book.ReadUrl = $"{BaseUrl}/br/?b={book.Id}";
                    book.DownloadUrl = $"{BaseUrl}/b/d/{book.Id}/fb2";
                }

                book.Author = ParseAuthor(row);

                book.ImageUrl = ParseImageUrl(row);

                book.Description = ParseDescription(row);

                book.Genre = ParseGenres(row);

                book.Language = "Русский";
                book.PageCount = ParsePageCount(row);
                book.IsCompleted = CheckIsCompleted(row);

                return book;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка парсинга книги: {ex.Message}");
                return null;
            }
        }

        private HtmlNode FindBookLink(HtmlNode row)
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

        private string ParseAuthor(HtmlNode row)
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

        private string ParseImageUrl(HtmlNode row)
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
                        return BuildFullUrl(url);
                }
            }

            return null;
        }

        private string ParseDescription(HtmlNode row)
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

        private string ParseGenres(HtmlNode row)
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

        private int? ParsePageCount(HtmlNode row)
        {
            var match = Regex.Match(row.InnerText, @"Страниц:\s*(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int pages))
                return pages;

            return null;
        }

        private bool CheckIsCompleted(HtmlNode row)
        {
            var text = row.InnerText;
            return text.Contains("Книга закончена") ||
                   text.Contains("Завершено") ||
                   text.Contains("Полный текст");
        }

        private string ParseBookContent(string bookUrl)
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

        private Book ParseFullBookInfo(string bookUrl)
        {
            var web = CreateHtmlWeb();

            try
            {
                var bookId = ExtractBookId(bookUrl);
                if (bookId == 0) return new Book { Id = 0, Title = "Ошибка", Description = "Не удалось извлечь ID книги" };

                var doc = web.Load(bookUrl);

                var book = new Book
                {
                    Id = bookId,
                    BookUrl = bookUrl,
                    ReadUrl = $"{BaseUrl}/br/?b={bookId}",
                    DownloadUrl = $"{BaseUrl}/b/d/{bookId}/fb2",
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
                        book.ImageUrl = BuildFullUrl(imgUrl);
                }

                var pageText = doc.DocumentNode.InnerText;
                var pagesMatch = Regex.Match(pageText, @"Страниц:\s*(\d+)");
                if (pagesMatch.Success && int.TryParse(pagesMatch.Groups[1].Value, out int pages))
                    book.PageCount = pages;

                book.IsCompleted = pageText.Contains("Книга закончена") ||
                                   pageText.Contains("Завершено") ||
                                   pageText.Contains("Полный текст");

                book.Content = ParseBookContent(book.ReadUrl);

                return book;
            }
            catch (Exception ex)
            {
                return new Book { Id = 0, Title = "Ошибка", Description = ex.Message, BookUrl = bookUrl };
            }
        }

        private object GetBookNavigation(string bookUrl)
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

        private int ExtractBookId(string url)
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

        private HtmlNodeCollection FindBookBlocks(HtmlDocument doc)
        {
            return doc.DocumentNode.SelectNodes("//div[@class='book_block']") ??
                   doc.DocumentNode.SelectNodes("//div[contains(@class, 'book-item')]") ??
                   doc.DocumentNode.SelectNodes("//tr[.//span[@itemprop='name']]");
        }

        private HtmlNodeCollection FindBookRows(HtmlDocument doc)
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

        private string BuildFullUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            if (url.StartsWith("//")) return "https:" + url;
            if (url.StartsWith("/")) return BaseUrl + url;
            if (!url.StartsWith("http")) return BaseUrl + "/" + url;

            return url;
        }

        private void RemoveUnwantedElements(HtmlNode container)
        {
            var removeSelectors = new[] { ".//script", ".//style", ".//div[contains(@class, 'ad')]" };

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
    }
}