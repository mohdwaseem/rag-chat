using System.ComponentModel.DataAnnotations;

namespace RAGDemoBackend.Models
{
    public class ClearModelRequest
    {
        [Required]
        public string ModelName { get; set; } = string.Empty;
    }
}
