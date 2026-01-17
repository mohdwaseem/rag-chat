using Microsoft.Extensions.Hosting;

namespace RAGDemoBackend.Services;

public sealed class QdrantInitializerHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QdrantInitializerHostedService> _logger;

    public QdrantInitializerHostedService(IServiceProvider serviceProvider, ILogger<QdrantInitializerHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app start serving basic endpoints even if Qdrant is unavailable.
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var vectorStore = scope.ServiceProvider.GetRequiredService<IVectorStoreService>();
            await vectorStore.InitializeCollectionAsync();
            _logger.LogInformation("Qdrant collection initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not connect to Qdrant. Start Qdrant with: docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant");
        }
    }
}
