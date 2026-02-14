namespace API_UP_02.Models
{
    public class Recommendations
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int BookId { get; set; }

        public string? BookTitle { get; set; }

        public string? BookAuthor { get; set; }

        public string? Reason { get; set; }
        public int RelevanceScore { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? ExpiryDate { get; set; }
        public bool IsViewed { get; set; }
        public bool IsAccepted { get; set; }
        public string? RecommendationType { get; set; }
        public Users? User { get; set; }
        public Book? Book { get; set; }
    }
}