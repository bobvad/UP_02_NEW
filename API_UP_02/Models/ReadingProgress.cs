using API_UP_02.Models;
using System.ComponentModel.DataAnnotations;

public class ReadingProgress
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int BookId { get; set; }
    public string Status { get; set; } 
    public int? CurrentPage { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? FinishDate { get; set; }
    public virtual Users User { get; set; }
    public virtual Book Book { get; set; }
}