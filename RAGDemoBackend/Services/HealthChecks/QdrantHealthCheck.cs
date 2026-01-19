using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qdrant.Client;

namespace RAGDemoBackend.Services.HealthChecks;

public sealed class QdrantHealthCheck : IHealthCheck
{
    private readonly QdrantClient _client;

    public QdrantHealthCheck(QdrantClient client)
    {
        _client = client;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.ListCollectionsAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Qdrant is unreachable", ex);
        }
    }
}
