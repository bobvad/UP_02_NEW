using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace API_UP_02.Models
{
    public class Leaderboard
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int Place { get; set; }
        public int BooksRead { get; set; }
        public int PagesRead { get; set; }
        public DateTime RecordDate { get; set; }
        public string? AchievementType { get; set; }
        public bool IsPeakPosition { get; set; }
        public virtual Users User { get; set; }
    }
}
