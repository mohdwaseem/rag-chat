namespace RAGDemoBackend.Models
{
    public class ChatRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MinLength(1)]
        public string Question { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public string? Language { get; set; }
    }
}
