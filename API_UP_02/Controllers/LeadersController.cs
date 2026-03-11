using API_UP_02.Context;
using API_UP_02.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API_UP_02.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "v1")]
    public class LeadersController : ControllerBase
    {
        private readonly BooksContext _context;

        public LeadersController(BooksContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Получает таблицу лидеров с цветовой индикацией
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLeaders([FromForm] int? currentUserId = null)
        {
            try
            {
                var users = await _context.Users
                    .Include(u => u.ReadingProgress)
                        .ThenInclude(rp => rp.Book)
                    .ToListAsync();

                var leaders = new List<object>();

                foreach (var user in users)
                {
                    var finishedBooks = user.ReadingProgress?
                        .Where(rp => rp.Status == "Прочитано" && rp.Book != null)
                        .ToList() ?? new List<ReadingProgress>();

                    var totalPages = finishedBooks.Sum(rp => rp.Book?.PageCount ?? 0);

                    leaders.Add(new
                    {
                        UserId = user.Id,
                        UserName = user.Login,
                        BooksRead = finishedBooks.Count,
                        PagesRead = totalPages
                    });
                }

                leaders = leaders.OrderByDescending(l => GetProp(l, "BooksRead"))
                                 .ThenByDescending(l => GetProp(l, "PagesRead"))
                                 .ToList();

                var result = new List<object>();
                for (int i = 0; i < leaders.Count; i++)
                {
                    var leader = leaders[i];
                    var place = i + 1;

                    var leaderObj = new
                    {
                        UserId = GetProp(leader, "UserId"),
                        UserName = GetProp(leader, "UserName"),
                        Place = place,
                        BooksRead = GetProp(leader, "BooksRead"),
                        PagesRead = GetProp(leader, "PagesRead"),
                        BackgroundColor = GetPlaceColor(place),
                        IsCurrentUser = currentUserId.HasValue && (int)GetProp(leader, "UserId") == currentUserId.Value
                    };

                    result.Add(leaderObj);

                    if (currentUserId.HasValue && (int)GetProp(leader, "UserId") == currentUserId.Value)
                    {
                        await SaveLeaderboardEntry(
                            currentUserId.Value,
                            place,
                            (int)GetProp(leader, "BooksRead"),
                            (int)GetProp(leader, "PagesRead")
                        );
                    }
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Получает позицию конкретного пользователя
        /// </summary>
        [HttpGet("user/{userId}/position")]
        public async Task<IActionResult> GetUserPosition(int userId)
        {
            try
            {
                var leadersResult = await GetLeaders(userId) as OkObjectResult;
                var allLeaders = leadersResult?.Value as List<object>;

                if (allLeaders == null)
                    return NotFound(new { Message = "Лидеры не найдены" });

                foreach (var leader in allLeaders)
                {
                    if ((int)GetProp(leader, "UserId") == userId)
                    {
                        return Ok(new
                        {
                            UserId = userId,
                            Place = GetProp(leader, "Place"),
                            UserName = GetProp(leader, "UserName"),
                            BooksRead = GetProp(leader, "BooksRead"),
                            PagesRead = GetProp(leader, "PagesRead"),
                            BackgroundColor = GetProp(leader, "BackgroundColor"),
                            IsCurrentUser = true
                        });
                    }
                }

                return NotFound(new { Message = "Пользователь не найден" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Сохраняет позицию пользователя в историю
        /// </summary>
        private async Task SaveLeaderboardEntry(int userId, int place, int booksRead, int pagesRead)
        {
            try
            {
                var today = DateTime.Today;
                var existingEntry = await _context.Leaderboards
                    .FirstOrDefaultAsync(e => e.UserId == userId && e.RecordDate.Date == today);

                if (existingEntry != null)
                {
                    existingEntry.Place = place;
                    existingEntry.BooksRead = booksRead;
                    existingEntry.PagesRead = pagesRead;

                    if (booksRead == 1 && string.IsNullOrEmpty(existingEntry.AchievementType))
                        existingEntry.AchievementType = "Первая книга ";
                }
                else
                {
                    var entry = new Leaderboard
                    {
                        UserId = userId,
                        Place = place,
                        BooksRead = booksRead,
                        PagesRead = pagesRead,
                        RecordDate = DateTime.Now
                    };

                    if (booksRead == 1)
                        entry.AchievementType = "Первая книга ";

                    _context.Leaderboards.Add(entry);
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения: {ex.Message}");
            }
        }

        /// <summary>
        /// Получает историю пользователя
        /// </summary>
        [HttpGet("user/{userId}/history")]
        public async Task<IActionResult> GetUserHistory(int userId)
        {
            try
            {
                var history = await _context.Leaderboards
                    .Where(e => e.UserId == userId)
                    .OrderByDescending(e => e.RecordDate)
                    .Take(10)
                    .Select(e => new
                    {
                        Date = e.RecordDate.ToString("dd.MM.yyyy"),
                        e.Place,
                        e.BooksRead,
                        Achievement = e.AchievementType
                    })
                    .ToListAsync();

                return Ok(history);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Обновляет статистику после прочтения книги
        /// </summary>
        [HttpPost("update-after-reading")]
        public async Task<IActionResult> UpdateAfterReading([FromBody] ReadingUpdateRequest update)
        {
            try
            {
                var userBooks = await _context.ReadingProgress
                    .Where(rp => rp.UserId == update.UserId && rp.Status == "Прочитано")
                    .CountAsync();

                if (userBooks == 1)
                {
                    var history = new Leaderboard
                    {
                        UserId = update.UserId,
                        Place = 0,
                        BooksRead = 1,
                        PagesRead = update.PagesRead,
                        RecordDate = DateTime.Now,
                        AchievementType = "Первая книга "
                    };
                    _context.Leaderboards.Add(history);
                    await _context.SaveChangesAsync();
                }

                return Ok(new { Message = "Статистика обновлена" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        private object GetProp(object obj, string prop) => obj.GetType().GetProperty(prop)?.GetValue(obj);

        private string GetPlaceColor(int place) => place switch
        {
            1 => "#FFD700",
            2 => "#C0C0C0",
            3 => "#CD7F32",
            _ => "#FFFFFF"
        };
    }

    public class ReadingUpdateRequest
    {
        public int UserId { get; set; }
        public int BookId { get; set; }
        public int PagesRead { get; set; }
        public DateTime FinishDate { get; set; }
    }
}