using Microsoft.AspNetCore.Mvc;
using API_UP_02.Context;
using API_UP_02.Models;
using Microsoft.EntityFrameworkCore;

namespace API_UP_02.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "v1")]
    public class Isbrannoe : ControllerBase
    {
        private readonly BooksContext _context;

        public Isbrannoe(BooksContext context)
        {
            _context = context;
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddToFavorites(int userId, int bookId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound("Пользователь не найден");
            }

            var book = await _context.Books.FindAsync(bookId);
            if (book == null)
            {
                return NotFound("Книга не найдена");
            }

            var existingFavorite = await _context.isbrannoes
                .FirstOrDefaultAsync(f => f.UserId == userId && f.BookId == bookId);

            if (existingFavorite != null)
            {
                return BadRequest("Эта книга уже в избранном");
            }

            var favorite = new Favorites
            {
                UserId = userId,
                BookId = bookId,
                AddedDate = DateTime.Now
            };

            await _context.isbrannoes.AddAsync(favorite);
            await _context.SaveChangesAsync();

            return Ok($"Книга \"{book.Title}\" добавлена в избранное");
        }

        [HttpDelete("remove")]
        public async Task<IActionResult> RemoveFromFavorites(int userId, int bookId)
        {
            var favorite = await _context.isbrannoes
                .FirstOrDefaultAsync(f => f.UserId == userId && f.BookId == bookId);

            if (favorite == null)
            {
                return NotFound("Эта книга не найдена в избранном");
            }

            _context.isbrannoes.Remove(favorite);
            await _context.SaveChangesAsync();

            return Ok("Книга удалена из избранного");
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserFavorites(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound("Пользователь не найден");
            }

            var favorites = await _context.isbrannoes
                .Where(f => f.UserId == userId)
                .Include(f => f.Book)  
                .ToListAsync();

            if (!favorites.Any())
            {
                return NotFound("У пользователя нет избранных книг");
            }

            return Ok(favorites);
        }

        [HttpGet("check")]
        public async Task<IActionResult> CheckFavorite(int userId, int bookId)
        {
            var exists = await _context.isbrannoes
                .AnyAsync(f => f.UserId == userId && f.BookId == bookId);

            return Ok(new { isFavorite = exists });
        }
    }
}