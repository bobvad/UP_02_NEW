using API_UP_02.Controllers;
using API_UP_02.Models;
using Microsoft.EntityFrameworkCore;

namespace API_UP_02.Context
{
    public class BooksContext:DbContext
    {
        public DbSet<Users> Users { get; set; }
        public DbSet<Book> Books { get; set; }
        public DbSet<Favorites> Favorites { get; set; }
        public BooksContext()
        {
            Database.EnsureCreated();
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseMySql("server=127.0.0.1;port=3306;uid=root;pwd=;database=For_Books", new MySqlServerVersion(new Version(8, 11, 0)));
        }
    }
}
