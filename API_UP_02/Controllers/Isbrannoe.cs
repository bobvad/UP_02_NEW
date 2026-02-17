using Microsoft.AspNetCore.Mvc;
using API_UP_02.Context;
using API_UP_02.Models;
using Microsoft.EntityFrameworkCore;

namespace API_UP_02.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "v1")]
    public class IsbrannoeController : ControllerBase
    {
        private readonly BooksContext _context;

        public IsbrannoeController(BooksContext context)
        {
            _context = context;
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddToFavorites(int userId, int bookId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound($"Пользователь с ID {userId} не найден");
                }

                var book = await _context.Books.FindAsync(bookId);
                if (book == null)
                {
                    return NotFound($"Книга с ID {bookId} не найдена");
                }

                var existingFavorite = await _context.Favorites
                    .FirstOrDefaultAsync(f => f.UserId == userId && f.BookId == bookId);

                if (existingFavorite != null)
                {
                    return BadRequest($"Книга \"{book.Title}\" уже находится в избранном");
                }

                var favorite = new Favorites
                {
                    UserId = userId,
                    BookId = bookId,
                    AddedDate = DateTime.Now
                };

                await _context.Favorites.AddAsync(favorite);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = $"Книга \"{book.Title}\" успешно добавлена в избранное",
                    favoriteId = favorite.Id
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при добавлении в избранное: {ex.Message}");
                return StatusCode(500, "Произошла внутренняя ошибка сервера");
            }
        }

        [HttpDelete("remove")]
        public async Task<IActionResult> RemoveFromFavorites(int userId, int bookId)
        {
            try
            {
                var favorite = await _context.Favorites
                    .FirstOrDefaultAsync(f => f.UserId == userId && f.BookId == bookId);

                if (favorite == null)
                {
                    return NotFound("Запись не найдена в избранном");
                }

                _context.Favorites.Remove(favorite);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Книга успешно удалена из избранного" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при удалении из избранного: {ex.Message}");
                return StatusCode(500, "Произошла внутренняя ошибка сервера");
            }
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserFavorites(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound($"Пользователь с ID {userId} не найден");
                }

                var favorites = await _context.Favorites
                    .Where(f => f.UserId == userId)
                    .Include(f => f.Book)
                    .OrderByDescending(f => f.AddedDate)
                    .ToListAsync();

                if (favorites == null || favorites.Count == 0)
                {
                    return Ok(new
                    {
                        message = "У пользователя пока нет избранных книг",
                        books = new List<object>()
                    });
                }

                var result = favorites.Select(f => new
                {
                    favoriteId = f.Id,
                    bookId = f.Book.Id,
                    bookTitle = f.Book.Title,
                    bookAuthor = f.Book.Author,
                    bookImage = f.Book.ImageUrl,
                    addedDate = f.AddedDate.ToString("dd.MM.yyyy HH:mm")
                });

                return Ok(new
                {
                    userId = userId,
                    userName = user.Login,
                    count = favorites.Count,
                    books = result
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении избранного: {ex.Message}");
                return StatusCode(500, "Произошла внутренняя ошибка сервера");
            }
        }

        [HttpGet("check")]
        public async Task<IActionResult> CheckFavorite(int userId, int bookId)
        {
            try
            {
                var exists = await _context.Favorites
                    .AnyAsync(f => f.UserId == userId && f.BookId == bookId);

                return Ok(new
                {
                    userId = userId,
                    bookId = bookId,
                    isFavorite = exists
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при проверке избранного: {ex.Message}");
                return StatusCode(500, "Произошла внутренняя ошибка сервера");
            }
        }

        [HttpGet("user/{userId}/count")]
        public async Task<IActionResult> GetFavoritesCount(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound($"Пользователь с ID {userId} не найден");
                }

                var count = await _context.Favorites
                    .CountAsync(f => f.UserId == userId);

                return Ok(new
                {
                    userId = userId,
                    favoritesCount = count
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при подсчете избранного: {ex.Message}");
                return StatusCode(500, "Произошла внутренняя ошибка сервера");
            }
        }

        [HttpDelete("user/{userId}/clear")]
        public async Task<IActionResult> ClearUserFavorites(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound($"Пользователь с ID {userId} не найден");
                }

                var favorites = await _context.Favorites
                    .Where(f => f.UserId == userId)
                    .ToListAsync();

                if (favorites.Any())
                {
                    _context.Favorites.RemoveRange(favorites);
                    await _context.SaveChangesAsync();

                    return Ok(new
                    {
                        message = $"Избранное пользователя очищено. Удалено книг: {favorites.Count}"
                    });
                }

                return Ok(new { message = "У пользователя нет избранных книг" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при очистке избранного: {ex.Message}");
                return StatusCode(500, "Произошла внутренняя ошибка сервера");
            }
        }
    }
}