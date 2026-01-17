namespace RAGDemoBackend.Services
{
    public interface IEmbeddingService
    {
        Task<float[]> GenerateEmbeddingAsync(string text);
        Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts);

        // Optional cancellation-enabled overloads for high-load scenarios.
        Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken);
        Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken);
    }
}
