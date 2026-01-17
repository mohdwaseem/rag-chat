namespace RAGDemoBackend.Models
{
    public class WebContentRequest
    {
        public string Url { get; set; } = string.Empty;
        public bool IncludeLinks { get; set; } = false;
        public int MaxDepth { get; set; } = 1;
        public string? FrameworkType { get; set; } = null; // "angular", "react", "vue", "static", or null
        public List<string>? CustomRoutes { get; set; } = null; // User-provided routes
    }

    public class WebContentResponse
    {
        public string Url { get; set; } = string.Empty;
        public int ChunksCreated { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<string> ProcessedUrls { get; set; } = new();
    }
}
