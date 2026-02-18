using API_UP_02.Context;
using API_UP_02.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API_UP_02.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "v1")]
    public class ReadingProgressController : Controller
    {
        private readonly BooksContext _context;

        public ReadingProgressController(BooksContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Получает весь прогресс чтения пользователя
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        /// <returns>Список книг с прогрессом</returns>
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserProgress(int userId)
        {
            try
            {
                var progress = await _context.ReadingProgress
                    .Include(rp => rp.Book)
                    .Where(rp => rp.UserId == userId)
                    .OrderByDescending(rp => rp.StartDate)
                    .ToListAsync();

                return Ok(progress);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Получает прогресс по конкретной книге для пользователя
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        /// <param name="bookId">ID книги</param>
        /// <returns>Прогресс чтения</returns>
        [HttpGet("user/{userId}/book/{bookId}")]
        public async Task<IActionResult> GetBookProgress(int userId, int bookId)
        {
            try
            {
                var progress = await _context.ReadingProgress
                    .Include(rp => rp.Book)
                    .FirstOrDefaultAsync(rp => rp.UserId == userId && rp.BookId == bookId);

                if (progress == null)
                    return NotFound(new { Message = "Прогресс по этой книге не найден" });

                return Ok(progress);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Добавляет книгу в список "Хочу прочитать"
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        /// <param name="bookId">ID книги</param>
        /// <returns>Созданный прогресс</returns>
        [HttpPost("want-to-read")]
        public async Task<IActionResult> AddToWantToRead([FromQuery] int userId, [FromQuery] int bookId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                    return NotFound(new { Message = "Пользователь не найден" });

                var book = await _context.Books.FindAsync(bookId);
                if (book == null)
                    return NotFound(new { Message = "Книга не найдена" });

                var existingProgress = await _context.ReadingProgress
                    .FirstOrDefaultAsync(rp => rp.UserId == userId && rp.BookId == bookId);

                if (existingProgress != null)
                {
                    return BadRequest(new { Message = "Книга уже есть в списке пользователя" });
                }

                var progress = new ReadingProgress
                {
                    UserId = userId,
                    BookId = bookId,
                    Status = "Хочу прочитать",
                    StartDate = DateTime.Now
                };

                _context.ReadingProgress.Add(progress);
                await _context.SaveChangesAsync();

                return Ok(progress);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Начинает чтение книги (меняет статус на "Читаю")
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        /// <param name="bookId">ID книги</param>
        /// <returns>Обновленный прогресс</returns>
        [HttpPut("start-reading")]
        public async Task<IActionResult> StartReading([FromQuery] int userId, [FromQuery] int bookId)
        {
            try
            {
                var progress = await _context.ReadingProgress
                    .FirstOrDefaultAsync(rp => rp.UserId == userId && rp.BookId == bookId);

                if (progress == null)
                {
                    progress = new ReadingProgress
                    {
                        UserId = userId,
                        BookId = bookId,
                        Status = "Читаю",
                        StartDate = DateTime.Now,
                        CurrentPage = 1
                    };
                    _context.ReadingProgress.Add(progress);
                }
                else
                {
                    progress.Status = "Читаю";
                    progress.StartDate ??= DateTime.Now;
                    progress.CurrentPage ??= 1;
                }

                await _context.SaveChangesAsync();

                return Ok(progress);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Обновляет текущую страницу при чтении
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        /// <param name="bookId">ID книги</param>
        /// <param name="page">Номер текущей страницы</param>
        /// <returns>Обновленный прогресс</returns>
        [HttpPut("update-page")]
        public async Task<IActionResult> UpdateCurrentPage([FromQuery] int userId, [FromQuery] int bookId, [FromQuery] int page)
        {
            try
            {
                var progress = await _context.ReadingProgress
                    .Include(rp => rp.Book)
                    .FirstOrDefaultAsync(rp => rp.UserId == userId && rp.BookId == bookId);

                if (progress == null)
                {
                    return NotFound(new { Message = "Прогресс по этой книге не найден. Начните чтение через /start-reading" });
                }

                if (progress.Status != "Читаю")
                {
                    progress.Status = "Читаю";
                }

                progress.CurrentPage = page;

                if (progress.Book != null && progress.Book.PageCount.HasValue && page >= progress.Book.PageCount.Value)
                {
                    progress.Status = "Прочитано";
                    progress.FinishDate = DateTime.Now;

                    await AwardExperienceForCompletingBook(userId, progress.Book);
                }

                await _context.SaveChangesAsync();

                return Ok(progress);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Отмечает книгу как прочитанную
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        /// <param name="bookId">ID книги</param>
        /// <returns>Обновленный прогресс</returns>
        [HttpPut("finish-book")]
        public async Task<IActionResult> FinishBook([FromQuery] int userId, [FromQuery] int bookId)
        {
            try
            {
                var progress = await _context.ReadingProgress
                    .Include(rp => rp.Book)
                    .FirstOrDefaultAsync(rp => rp.UserId == userId && rp.BookId == bookId);

                if (progress == null)
                {
                    return NotFound(new { Message = "Прогресс по этой книге не найден" });
                }

                progress.Status = "Прочитано";
                progress.FinishDate = DateTime.Now;

                if (progress.Book != null && progress.Book.PageCount.HasValue)
                {
                    progress.CurrentPage = progress.Book.PageCount.Value;
                }

                await AwardExperienceForCompletingBook(userId, progress.Book);

                await _context.SaveChangesAsync();

                return Ok(progress);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Удаляет книгу из списка пользователя
        /// </summary>
        /// <param name="id">ID записи прогресса</param>
        /// <returns>Результат удаления</returns>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProgress(int id)
        {
            try
            {
                var progress = await _context.ReadingProgress.FindAsync(id);
                if (progress == null)
                    return NotFound(new { Message = "Запись не найдена" });

                _context.ReadingProgress.Remove(progress);
                await _context.SaveChangesAsync();

                return Ok(new { Message = "Запись удалена" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Получает статистику чтения пользователя
        /// </summary>
        /// <param name="userId">ID пользователя</param>
        /// <returns>Статистика чтения</returns>
        [HttpGet("stats/{userId}")]
        public async Task<IActionResult> GetReadingStats(int userId)
        {
            try
            {
                var allProgress = await _context.ReadingProgress
                    .Include(rp => rp.Book)
                    .Where(rp => rp.UserId == userId)
                    .ToListAsync();

                var stats = new
                {
                    WantToRead = allProgress.Count(rp => rp.Status == "Хочу прочитать"),
                    CurrentlyReading = allProgress.Count(rp => rp.Status == "Читаю"),
                    Completed = allProgress.Count(rp => rp.Status == "Прочитано"),
                    TotalBooks = allProgress.Count,

                    CompletedBooks = allProgress
                        .Where(rp => rp.Status == "Прочитано" && rp.Book != null)
                        .Select(rp => new
                        {
                            rp.Book.Id,
                            rp.Book.Title,
                            rp.Book.Author,
                            rp.FinishDate
                        })
                        .ToList(),

                    ReadingNow = allProgress
                        .Where(rp => rp.Status == "Читаю" && rp.Book != null)
                        .Select(rp => new
                        {
                            rp.Book.Id,
                            rp.Book.Title,
                            rp.Book.Author,
                            rp.CurrentPage,
                            TotalPages = rp.Book.PageCount,
                            ProgressPercent = rp.Book.PageCount.HasValue && rp.Book.PageCount > 0
                                ? (int)((double)(rp.CurrentPage ?? 0) / rp.Book.PageCount.Value * 100)
                                : 0
                        })
                        .ToList()
                };

                return Ok(stats);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Вспомогательный метод для начисления опыта за прочтение книги
        /// </summary>
        private async Task AwardExperienceForCompletingBook(int userId, Book book)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user != null && book != null)
                {
                    int experienceGained = (book.PageCount ?? 100) * 10;
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при начислении опыта: {ex.Message}");
            }
        }
    }
}