namespace API_UP_02.Models
{
    public class Favorites
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int BookId { get; set; }
        public DateTime AddedDate { get; set; } = DateTime.Now;
        public Users? User { get; set; }
        public Book? Book { get; set; }
    }
}