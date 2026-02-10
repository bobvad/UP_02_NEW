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
        /// <param name="count">Сколько книг получить (максимум 100)</param>
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
        /// Получить популярные книги по жанрам
        /// </summary>
        /// <param name="genre">Жанр (фэнтези, детектив, роман)</param>
        /// <param name="count">Количество книг</param>
        /// <returns>Список книг</returns>
        [HttpGet("books/genre")]
        public IActionResult GetBooksByGenre([FromQuery] string genre = "фэнтези", [FromQuery] int count = 30)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(genre))
                {
                    return BadRequest("Укажите жанр");
                }

                if (count > 100) count = 100;

                var books = ParseBooksByGenre(genre, count);
                return Ok(books);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new List<Book>());
            }
        }

        private List<Book> ParseBooksByGenre(string genre, int count)
        {
            var books = new List<Book>();
            var web = new HtmlWeb();
            web.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

            try
            {
                var encodedGenre = Uri.EscapeDataString(genre.ToLower());
                var genreUrl = $"https://litmir.club/knigi/zhanry/{encodedGenre}";

                Console.WriteLine($"Ищем книги жанра '{genre}' по URL: {genreUrl}");

                var doc = web.Load(genreUrl);

                var bookRows = FindBookRows(doc);

                if (bookRows == null || bookRows.Count == 0)
                {
                    Console.WriteLine($"Книги жанра '{genre}' не найдены");
                    return books;
                }

                Console.WriteLine($"Найдено {bookRows.Count} книг жанра '{genre}'");

                int parsedCount = 0;
                foreach (var row in bookRows)
                {
                    if (parsedCount >= count) break;

                    var book = ParseBook(row);
                    if (book != null && !string.IsNullOrEmpty(book.Title))
                    {
                        books.Add(book);
                        parsedCount++;
                    }
                }

                return books;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка парсинга по жанру: {ex.Message}");
                return new List<Book>();
            }
        }

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