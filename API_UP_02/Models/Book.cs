namespace API_UP_02.Models
{
    public class Book
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Genre { get; set; }
        public string ImageUrl { get; set; }
        public string Description { get; set; }
        public string Content { get; set; }
        public string Language { get; set; }
        public int? PageCount { get; set; }
        public bool IsCompleted { get; set; }

        /// <summary>
        /// URL страницы книги на сайте
        /// </summary>
        public string BookUrl { get; set; }

        /// <summary>
        /// URL для чтения книги
        /// </summary>
        public string ReadUrl { get; set; }

        /// <summary>
        /// URL для скачивания книги
        /// </summary>
        public string DownloadUrl { get; set; }
    }
}