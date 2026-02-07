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
        /// Получить все книги с сайта Litmir
        /// </summary>
        /// <returns>Список книг</returns>
        [HttpGet("books")]
        public IActionResult GetBooks()
        {
            try
            {
                var books = ParseBooksFromLitmir();
                return Ok(books);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return Ok(new List<Book>());
            }
        }

        private List<Book> ParseBooksFromLitmir()
        {
            var books = new List<Book>();
            var web = new HtmlWeb();
            web.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

            try
            {
                var doc = web.Load("https://litmir.club/");

                var bookRows = doc.DocumentNode.SelectNodes("//table[@class='island']//tr");

                if (bookRows == null)
                {
                    bookRows = doc.DocumentNode.SelectNodes("//tr[.//span[@itemprop='name']]");
                }

                if (bookRows == null || bookRows.Count == 0)
                {
                    Console.WriteLine("Книги не найдены на странице");
                    return books;
                }

                Console.WriteLine($"Найдено {bookRows.Count} книг");

                foreach (var row in bookRows)
                {
                    var book = ParseBook(row);
                    if (book != null && !string.IsNullOrEmpty(book.Title))
                    {
                        books.Add(book);
                    }
                }

                Console.WriteLine($"Успешно спарсено {books.Count} книг");
                return books;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка парсинга: {ex.Message}");
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
                    return null;
                }

                book.Title = titleNode.InnerText.Trim();

                var authorNode = row.SelectSingleNode(".//a[contains(@href, '/a/?id=')]");
                if (authorNode == null)
                {
                    authorNode = row.SelectSingleNode(".//span[@class='desc2']//a");
                }

                book.Author = authorNode?.InnerText?.Trim() ?? "Неизвестен";

                var imgNode = row.SelectSingleNode(".//img[@class='lt32']");

                if (imgNode == null)
                {
                    imgNode = row.SelectSingleNode(".//img[@data-src]");
                }

                if (imgNode == null)
                {
                    imgNode = row.SelectSingleNode(".//img[contains(@src, '/data/Book/')]");
                }

                if (imgNode == null)
                {
                    imgNode = row.SelectSingleNode(".//img");
                }

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
                    book.Description = text;
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
                                   row.InnerText.Contains("Завершено");

                return book;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка парсинга книги: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Получить книги с поиском
        /// </summary>
        /// <param name="query">Поисковый запрос</param>
        /// <returns>Список книг</returns>
        [HttpGet("search")]
        public IActionResult SearchBooks([FromQuery] string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return BadRequest("Укажите поисковый запрос");
                }

                var books = ParseBooksBySearch(query);
                return Ok(books);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка поиска: {ex.Message}");
                return Ok(new List<Book>());
            }
        }

        private List<Book> ParseBooksBySearch(string query)
        {
            var books = new List<Book>();
            var web = new HtmlWeb();
            web.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

            try
            {
                var encodedQuery = Uri.EscapeDataString(query);
                var searchUrl = $"https://litmir.club/search?q={encodedQuery}";

                Console.WriteLine($"Ищем книги по запросу: {query}");
                Console.WriteLine($"URL: {searchUrl}");

                var doc = web.Load(searchUrl);

                var bookRows = doc.DocumentNode.SelectNodes("//table[@class='island']//tr");

                if (bookRows == null)
                {
                    bookRows = doc.DocumentNode.SelectNodes("//tr[.//span[@itemprop='name']]");
                }

                if (bookRows == null || bookRows.Count == 0)
                {
                    Console.WriteLine("Книги по запросу не найдены");
                    return books;
                }

                Console.WriteLine($"Найдено {bookRows.Count} книг по запросу '{query}'");

                foreach (var row in bookRows)
                {
                    var book = ParseBook(row);
                    if (book != null && !string.IsNullOrEmpty(book.Title))
                    {
                        books.Add(book);
                    }
                }

                return books;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка поиска: {ex.Message}");
                return new List<Book>();
            }
        }
    }
}