using API_UP_02.Models;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.RegularExpressions;

namespace API_UP_02.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "v5")]
    public class ParsingBooksController : ControllerBase
    {
        /// <summary>
        /// Получить много книг с сайта Litmir
        /// </summary>
        /// <param name="count">Сколько книг получить 100 книг</param>
        /// <returns>Список книг</returns>
        [HttpGet("books/many")]
        public IActionResult GetManyBooks([FromQuery] int count = 50)
        {
            try
            {
                var books = ParseManyBooksFromLitmir(count);
                return Ok(books);
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
        /// <param name="pages">Сколько страниц парсить (1 страница = ~20 книг)</param>
        /// <returns>Список книг</returns>
        [HttpGet("books/pages")]
        public IActionResult GetBooksFromPages([FromQuery] int pages = 3)
        {
            try
            {
                if (pages > 5)
                {
                    pages = 5;
                    Console.WriteLine($"Слишком много страниц, ограничиваем до {pages}");
                }

                var books = ParseBooksFromMultiplePages(pages);
                return Ok(books);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new List<Book>());
            }
        }

        /// <summary>
        /// Парсинг книг со всего сайта
        /// </summary>
        private List<Book> ParseManyBooksFromLitmir(int targetCount)
        {
            var allBooks = new List<Book>();
            var web = new HtmlWeb();
            web.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

            try
            {
                var baseUrl = "https://litmir.club/";
                var currentPage = 1;

                while (allBooks.Count < targetCount)
                {
                    Console.WriteLine($"Парсим страницу {currentPage}...");

                    var pageUrl = currentPage == 1 ? baseUrl : $"{baseUrl}?page={currentPage}";
                    var doc = web.Load(pageUrl);
                    var bookRows = FindBookRows(doc);

                    if (bookRows == null || bookRows.Count == 0)
                    {
                        Console.WriteLine("Больше книг не найдено");
                        break;
                    }

                    Console.WriteLine($"Найдено {bookRows.Count} книг на странице {currentPage}");

                    var pageBooks = ParseBooksFromRows(bookRows, targetCount - allBooks.Count);
                    allBooks.AddRange(pageBooks);

                    Console.WriteLine($"Всего собрано {allBooks.Count} из {targetCount} книг");

                    currentPage++;

                    if (currentPage > 10)
                    {
                        Console.WriteLine("Достигнут лимит страниц");
                        break;
                    }
                }

                Console.WriteLine($"Успешно собрано {allBooks.Count} книг");
                return allBooks;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка парсинга: {ex.Message}");
                return allBooks;
            }
        }

        /// <summary>
        /// Парсинг книг с нескольких страниц
        /// </summary>
        private List<Book> ParseBooksFromMultiplePages(int pagesCount)
        {
            var allBooks = new List<Book>();
            var web = new HtmlWeb();
            web.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

            try
            {
                for (int pageNum = 1; pageNum <= pagesCount; pageNum++)
                {
                    Console.WriteLine($"Парсим страницу {pageNum} из {pagesCount}...");

                    var pageUrl = pageNum == 1 ?
                        "https://litmir.club/" :
                        $"https://litmir.club/knigi?page={pageNum}";

                    var doc = web.Load(pageUrl);
                    var bookRows = FindBookRows(doc);

                    if (bookRows == null || bookRows.Count == 0)
                    {
                        Console.WriteLine($"На странице {pageNum} книги не найдены");
                        continue;
                    }

                    Console.WriteLine($"На странице {pageNum} найдено {bookRows.Count} книг");

                    foreach (var row in bookRows)
                    {
                        var book = ParseBook(row);
                        if (book != null && !string.IsNullOrEmpty(book.Title))
                        {
                            allBooks.Add(book);
                        }
                    }

                    if (pageNum < pagesCount)
                    {
                        Console.WriteLine("Пауза 2 секунды перед следующим запросом...");
                        Thread.Sleep(2000);
                    }
                }

                Console.WriteLine($"Успешно спарсено {allBooks.Count} книг с {pagesCount} страниц");
                return allBooks;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка парсинга нескольких страниц: {ex.Message}");
                return allBooks;
            }
        }

        /// <summary>
        /// Поиск строк с книгами на странице
        /// </summary>
        private HtmlNodeCollection FindBookRows(HtmlDocument doc)
        {
            var bookRows = doc.DocumentNode.SelectNodes("//table[@class='']//tr");

            if (bookRows == null)
            {
                bookRows = doc.DocumentNode.SelectNodes("//tr[.//span[@itemprop='name']]");
            }

            if (bookRows == null)
            {
                bookRows = doc.DocumentNode.SelectNodes("//div[contains(@class, 'book-item')]");
            }

            if (bookRows == null)
            {
                bookRows = doc.DocumentNode.SelectNodes("//div[@class='book_block']");
            }

            return bookRows;
        }

        /// <summary>
        /// Парсинг книг из найденных строк
        /// </summary>
        private List<Book> ParseBooksFromRows(HtmlNodeCollection rows, int maxCount)
        {
            var books = new List<Book>();

            foreach (var row in rows)
            {
                if (books.Count >= maxCount)
                {
                    break;
                }

                var book = ParseBook(row);
                if (book != null && !string.IsNullOrEmpty(book.Title))
                {
                    books.Add(book);
                }
            }

            return books;
        }

        /// <summary>
        /// Парсинг жанров из карточки книги 
        /// </summary>
        private string ParseGenresFromBookCard(HtmlNode bookNode)
        {
            try
            {
                var genreSpan = bookNode.SelectSingleNode(".//span[@itemprop='genre']");

                if (genreSpan == null)
                {
                    return "Жанр не указан";
                }
                var genres = new List<string>();

                var genreLinks = genreSpan.SelectNodes(".//a[not(contains(@id, 'more'))]");
                if (genreLinks != null)
                {
                    foreach (var link in genreLinks)
                    {
                        var genreText = link.InnerText.Trim();
                        if (!string.IsNullOrEmpty(genreText) && genreText != "...")
                        {
                            genres.Add(genreText);
                        }
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
                            var genreText = link.InnerText.Trim();
                            if (!string.IsNullOrEmpty(genreText))
                            {
                                genres.Add(genreText);
                            }
                        }
                    }
                }

                return genres.Count > 0 ? string.Join(", ", genres) : "Жанр не указан";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при парсинге жанров: {ex.Message}");
                return "Ошибка парсинга жанров";
            }
        }
        /// <summary>
        /// Парсинг страницы одной книги
        /// </summary>
        private Book ParsePageBook(HtmlNode pageNode)
        {
            var book = new Book();

            try
            {
                var titleNode = pageNode.SelectSingleNode(".//h1[@itemprop='name']");
                if (titleNode != null)
                {
                    book.Title = WebUtility.HtmlDecode(titleNode.InnerText.Trim());
                }

                var authorNode = pageNode.SelectSingleNode(".//a[@itemprop='author']");
                if (authorNode != null)
                {
                    book.Author = WebUtility.HtmlDecode(authorNode.InnerText.Trim());
                }

                var genreSpan = pageNode.SelectSingleNode(".//div[@class='page_text']//a");
                if (genreSpan != null)
                {
                    book.Genre = WebUtility.HtmlDecode(genreSpan.InnerText.Trim());
                }
                else
                {
                    var genreNodes = pageNode.SelectNodes(".//span[@itemprop='genre']//a");
                    if (genreNodes != null)
                    {
                        var genres = new List<string>();
                        foreach (var genreNode in genreNodes)
                        {
                            string genre = genreNode.InnerText.Trim();
                            if (!string.IsNullOrEmpty(genre) && genre != "...")
                            {
                                genres.Add(genre);
                            }
                        }
                        book.Genre = string.Join(", ", genres);
                    }
                    else
                    {
                        book.Genre = "Жанр не указан";
                    }
                }

                var descNode = pageNode.SelectSingleNode(".//div[@itemprop='description']");
                if (descNode != null)
                {
                    book.Description = WebUtility.HtmlDecode(descNode.InnerText.Trim());
                }

                var imgNode = pageNode.SelectSingleNode(".//img[@itemprop='image']");
                if (imgNode != null)
                {
                    string imgUrl = imgNode.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(imgUrl))
                    {
                        if (imgUrl.StartsWith("//"))
                        {
                            imgUrl = "https:" + imgUrl;
                        }
                        else if (imgUrl.StartsWith("/"))
                        {
                            imgUrl = "https://litmir.club" + imgUrl;
                        }
                        book.ImageUrl = imgUrl;
                    }
                }
                book.Language = "Русский";

                Console.WriteLine($"Книга спарсена: {book.Title}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при парсинге книги: {ex.Message}");
            }

            return book;
        }
        /// <summary>
        /// Парсинг одной книги (С ЖАНРАМИ)
        /// </summary>
        private Book ParseBook(HtmlNode row)
        {
            try
            {
                var book = new Book();

                var titleNode = row.SelectSingleNode(".//span[@itemprop='name']");
                if (titleNode == null)
                {
                    titleNode = row.SelectSingleNode(".//div[@class='book_name']//a");
                }
                if (titleNode == null)
                {
                    titleNode = row.SelectSingleNode(".//a[@class='book_name']");
                }

                if (titleNode == null)
                {
                    return null; 
                }

                book.Title = WebUtility.HtmlDecode(titleNode.InnerText.Trim());

                var authorNode = row.SelectSingleNode(".//a[contains(@href, '/a/?id=')]");
                if (authorNode == null)
                {
                    authorNode = row.SelectSingleNode(".//span[@class='desc2']//a");
                }
                if (authorNode == null)
                {
                    authorNode = row.SelectSingleNode(".//div[@class='author']//a");
                }

                book.Author = authorNode != null ?
                    WebUtility.HtmlDecode(authorNode.InnerText.Trim()) :
                    "Неизвестен";

                var imgNode = row.SelectSingleNode(".//img[@class='lt32']");
                if (imgNode == null) imgNode = row.SelectSingleNode(".//img[@data-src]");
                if (imgNode == null) imgNode = row.SelectSingleNode(".//img[contains(@src, '/data/Book/')]");
                if (imgNode == null) imgNode = row.SelectSingleNode(".//img");

                if (imgNode != null)
                {
                    book.ImageUrl = imgNode.GetAttributeValue("data-src", "");
                    if (string.IsNullOrEmpty(book.ImageUrl))
                    {
                        book.ImageUrl = imgNode.GetAttributeValue("src", "");
                    }

                    if (!string.IsNullOrEmpty(book.ImageUrl))
                    {
                        if (book.ImageUrl.StartsWith("//"))
                        {
                            book.ImageUrl = "https:" + book.ImageUrl;
                        }
                        else if (book.ImageUrl.StartsWith("/"))
                        {
                            book.ImageUrl = "https://litmir.club" + book.ImageUrl;
                        }
                    }
                }

                var descNode = row.SelectSingleNode(".//div[@class='description']");
                if (descNode != null)
                {
                    var cleanDesc = descNode.CloneNode(true);
                    var techDivs = cleanDesc.SelectNodes(".//div[@style]");
                    if (techDivs != null)
                    {
                        foreach (var div in techDivs)
                        {
                            div.Remove();
                        }
                    }

                    var text = cleanDesc.InnerText.Trim();
                    text = Regex.Replace(text, @"\s+", " ");
                    book.Description = WebUtility.HtmlDecode(text);
                }
                else
                {
                    book.Description = "Описание отсутствует";
                }

                book.Genre = ParseGenresFromBookCard(row);

                book.Language = "Русский";

                var pagesMatch = Regex.Match(row.InnerText, @"Страниц:\s*(\d+)");
                if (pagesMatch.Success && int.TryParse(pagesMatch.Groups[1].Value, out int pages))
                {
                    book.PageCount = pages;
                }

                book.IsCompleted = row.InnerText.Contains("Книга закончена") ||
                                   row.InnerText.Contains("Завершено") ||
                                   row.InnerText.Contains("Полный текст");

                return book;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка парсинга книги: {ex.Message}");
                return null;
            }
        }
    }
}