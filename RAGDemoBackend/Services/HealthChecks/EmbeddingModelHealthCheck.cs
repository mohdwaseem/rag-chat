using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RAGDemoBackend.Services.HealthChecks;

public sealed class EmbeddingModelHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public EmbeddingModelHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var modelPath = _configuration["Embeddings:ModelPath"] ?? Path.Combine("Models", "model.onnx");
        modelPath = Path.IsPathRooted(modelPath) ? modelPath : Path.Combine(Directory.GetCurrentDirectory(), modelPath);

        if (File.Exists(modelPath))
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }

        return Task.FromResult(HealthCheckResult.Unhealthy($"Embedding model file not found at '{modelPath}'"));
    }
}
