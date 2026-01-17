using Qdrant.Client;
using Qdrant.Client.Grpc;
using RAGDemoBackend.Models;

namespace RAGDemoBackend.Services
{
    public interface IVectorStoreService
    {
        Task InitializeCollectionAsync();
        Task<bool> UpsertDocumentChunksAsync(List<DocumentChunk> chunks, List<float[]> embeddings);
        Task<List<(DocumentChunk Chunk, float Score)>> SearchSimilarChunksAsync(float[] queryEmbedding, int limit = 5, string? language = null);
        Task<bool> DeleteDocumentAsync(string documentSource);
        Task<int> GetDocumentCountAsync();
    }

    public class QdrantVectorStoreService : IVectorStoreService
    {
        private readonly QdrantClient _client;
        private readonly IConfiguration _configuration;
        private readonly ILogger<QdrantVectorStoreService> _logger;
        private readonly string _collectionName;
        private readonly Dictionary<ulong, DocumentChunk> _chunkCache;

        public QdrantVectorStoreService(
            IConfiguration configuration, 
            ILogger<QdrantVectorStoreService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _collectionName = configuration["Qdrant:CollectionName"] ?? "documents";
            _chunkCache = new Dictionary<ulong, DocumentChunk>();

            var host = configuration["Qdrant:Host"] ?? "localhost";
            var port = int.Parse(configuration["Qdrant:Port"] ?? "6334");
            var useHttps = bool.Parse(configuration["Qdrant:UseHttps"] ?? "false");

            _client = new QdrantClient(host, port, useHttps);
            _logger.LogInformation("Qdrant client initialized: {Host}:{Port}", host, port);
        }

        public async Task InitializeCollectionAsync()
        {
            try
            {
                // Check if collection exists
                var collections = await _client.ListCollectionsAsync();
                var exists = collections.Contains(_collectionName);

                if (!exists)
                {
                    // Create collection with 384 dimensions (all-MiniLM-L6-v2)
                    await _client.CreateCollectionAsync(
                        collectionName: _collectionName,
                        vectorsConfig: new VectorParams
                        {
                            Size = 384,
                            Distance = Distance.Cosine
                        }
                    );
                    _logger.LogInformation("Created Qdrant collection: {CollectionName}", _collectionName);
                }
                else
                {
                    _logger.LogInformation("Qdrant collection already exists: {CollectionName}", _collectionName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Qdrant collection. Make sure Qdrant is running.");
                throw;
            }
        }

        public async Task<bool> UpsertDocumentChunksAsync(List<DocumentChunk> chunks, List<float[]> embeddings)
        {
            List<PointStruct> points = new List<PointStruct>();
            
            try
            {
                if (chunks.Count != embeddings.Count)
                {
                    throw new ArgumentException("Chunks and embeddings count must match");
                }

                // Log embedding dimension for diagnostics
                if (embeddings.Count > 0)
                {
                    _logger.LogInformation("Upserting {Count} chunks with embedding dimension: {Dimension}", 
                        chunks.Count, embeddings[0].Length);
                }

                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];
                    var pointId = (ulong)(chunk.Id.GetHashCode() & 0x7FFFFFFF);
                    
                    // Cache the chunk for retrieval
                    _chunkCache[pointId] = chunk;

                    var point = new PointStruct
                    {
                        Id = new PointId { Num = pointId },
                        Vectors = embeddings[i],
                        Payload =
                        {
                            ["content"] = chunk.Content,
                            ["source"] = chunk.Source,
                            ["index"] = chunk.Index,
                            ["chunkId"] = chunk.Id.ToString(),
                            ["language"] = chunk.Metadata.TryGetValue("language", out var lang) ? lang : "en"
                        }
                    };

                    points.Add(point);
                }

                await _client.UpsertAsync(_collectionName, points);
                _logger.LogInformation("Upserted {Count} chunks to Qdrant", chunks.Count);
                return true;
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                _logger.LogWarning("Collection not found. Recreating collection and retrying...");
                
                // Recreate the collection
                await InitializeCollectionAsync();
                
                // Retry the upsert
                try
                {
                    await _client.UpsertAsync(_collectionName, points);
                    _logger.LogInformation("Successfully upserted {Count} chunks after recreating collection", chunks.Count);
                    return true;
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "Failed to upsert after recreating collection");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upsert document chunks to Qdrant");
                return false;
            }
        }

        public async Task<List<(DocumentChunk Chunk, float Score)>> SearchSimilarChunksAsync(
            float[] queryEmbedding, 
            int limit = 5,
            string? language = null)
        {
            try
            {
                // Get minimum relevance score from configuration
                var minRelevanceScore = _configuration.GetValue<float>("DemoSettings:MinRelevanceScore", 0.3f);
                
                _logger.LogInformation("Searching Qdrant with limit={Limit}, scoreThreshold={Threshold}", 
                    limit, minRelevanceScore);
                
                Filter? filter = null;
                if (!string.IsNullOrWhiteSpace(language))
                {
                    filter = new Filter
                    {
                        Must =
                        {
                            new Condition
                            {
                                Field = new FieldCondition
                                {
                                    Key = "language",
                                    Match = new Match { Keyword = language }
                                }
                            }
                        }
                    };
                }

                var results = await _client.SearchAsync(
                    collectionName: _collectionName,
                    vector: queryEmbedding,
                    limit: (ulong)limit,
                    scoreThreshold: minRelevanceScore,
                    filter: filter
                );

                var chunks = new List<(DocumentChunk, float)>();

                _logger.LogInformation("Qdrant returned {Count} results", results.Count);

                foreach (var result in results)
                {
                    var pointId = result.Id.Num;
                    
                    _logger.LogDebug("Result: Score={Score}, PointId={PointId}", result.Score, pointId);
                    
                    // Try to get from cache first
                    if (_chunkCache.TryGetValue(pointId, out var cachedChunk))
                    {
                        chunks.Add((cachedChunk, result.Score));
                        _logger.LogDebug("Found chunk from cache: {Source} (Index {Index})", 
                            cachedChunk.Source, cachedChunk.Index);
                    }
                    else
                    {
                        // Reconstruct from payload
                        var chunk = new DocumentChunk
                        {
                            Id = Guid.Parse(result.Payload["chunkId"].StringValue),
                            Content = result.Payload["content"].StringValue,
                            Source = result.Payload["source"].StringValue,
                            Index = (int)result.Payload["index"].IntegerValue,
                            Metadata = new Dictionary<string, string>
                            {
                                { "language", result.Payload["language"].StringValue }
                            }
                        };
                        
                        _chunkCache[pointId] = chunk;
                        chunks.Add((chunk, result.Score));
                        _logger.LogDebug("Reconstructed chunk from payload: {Source} (Index {Index})", 
                            chunk.Source, chunk.Index);
                    }
                }

                if (chunks.Count > 0)
                {
                    _logger.LogInformation("Found {Count} similar chunks with scores: {Scores}", 
                        chunks.Count, string.Join(", ", chunks.Select(c => $"{c.Item2:F3}")));
                }
                else
                {
                    _logger.LogWarning("No chunks found matching the query with threshold >= {Threshold}", 
                        minRelevanceScore);
                }
                return chunks;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search Qdrant");
                return new List<(DocumentChunk, float)>();
            }
        }

        public async Task<bool> DeleteDocumentAsync(string documentSource)
        {
            try
            {
                var filter = new Filter
                {
                    Must =
                    {
                        new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key = "source",
                                Match = new Match { Keyword = documentSource }
                            }
                        }
                    }
                };

                await _client.DeleteAsync(_collectionName, filter);
                
                // Clean cache
                var keysToRemove = _chunkCache
                    .Where(kvp => kvp.Value.Source == documentSource)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in keysToRemove)
                {
                    _chunkCache.Remove(key);
                }

                _logger.LogInformation("Deleted document chunks for source: {Source}", documentSource);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete document from Qdrant");
                return false;
            }
        }

        public async Task<int> GetDocumentCountAsync()
        {
            try
            {
                var info = await _client.GetCollectionInfoAsync(_collectionName);
                return (int)(info.PointsCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get document count from Qdrant");
                return 0;
            }
        }
    }
}
