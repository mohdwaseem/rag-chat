namespace RAGDemoBackend.Models
{
    public class DocumentChunk
    {
        public Guid Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty; // Just filename or URL
        public string? FilePath { get; set; } // Full path
        public int Index { get; set; }
        public SourceType SourceType { get; set; } = SourceType.Unknown;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
