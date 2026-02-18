using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API_UP_02.Models
{
    public class AIInteraction
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string PromptType { get; set; } 
        public string InputText { get; set; }
        public string OutputText { get; set; }
        public string ModelUsed { get; set; } = "GigaChat";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int? BookId { get; set; }
        public virtual Users User { get; set; }
        public virtual Book Book { get; set; }
    }
}