namespace API_UP_02.Models
{
    public class Book
    {
        public string Title { get; set; }
        public string Author { get; set; }
        public string ImageUrl { get; set; }
        public string Description { get; set; }
        public string Language { get; set; }
        public int? PageCount { get; set; }
        public bool IsCompleted { get; set; }
    }
}
