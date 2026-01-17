using System.Numerics.Tensors;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace RAGDemoBackend.Services
{
    /// <summary>
    /// Local embedding service using ONNX Runtime with all-MiniLM-L6-v2 model
    /// This is completely free and runs locally without any API calls
    /// </summary>
    public class LocalEmbeddingService : IEmbeddingService, IDisposable
    {
        private readonly InferenceSession? _session;
        private readonly Dictionary<string, int> _vocabulary;
        private readonly ILogger<LocalEmbeddingService> _logger;
        private readonly bool _isModelAvailable;
        private const int MaxTokens = 128;
        private const int EmbeddingDimension = 384;

        public LocalEmbeddingService(ILogger<LocalEmbeddingService> logger)
        {
            _logger = logger;
            _vocabulary = BuildVocabulary();
            
            try
            {
                var modelPath = Path.Combine(Directory.GetCurrentDirectory(), "Models", "model.onnx");
                
                if (File.Exists(modelPath))
                {
                    _session = new InferenceSession(modelPath);
                    _isModelAvailable = true;
                    _logger.LogInformation("ONNX model loaded successfully from {ModelPath}", modelPath);
                }
                else
                {
                    _isModelAvailable = false;
                    _logger.LogWarning("ONNX model not found at {ModelPath}. Using fallback TF-IDF embeddings.", modelPath);
                    _logger.LogInformation("To use real embeddings, download all-MiniLM-L6-v2 ONNX model to Models folder");
                }
            }
            catch (Exception ex)
            {
                _isModelAvailable = false;
                _logger.LogError(ex, "Failed to load ONNX model. Using fallback embeddings.");
            }
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            return await GenerateEmbeddingAsync(text, CancellationToken.None);
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new float[EmbeddingDimension];
            }

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_isModelAvailable && _session != null)
                {
                    return GenerateOnnxEmbedding(text);
                }

                return GenerateFallbackEmbedding(text);
            }, cancellationToken);
        }

        public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts)
        {
            return await GenerateEmbeddingsAsync(texts, CancellationToken.None);
        }

        public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken)
        {
            if (texts is null)
            {
                throw new ArgumentNullException(nameof(texts));
            }

            if (texts.Count == 0)
            {
                return new List<float[]>();
            }

            // Bound concurrency to avoid CPU starvation under load.
            var maxConcurrency = Math.Max(1, Environment.ProcessorCount / 2);
            using var gate = new SemaphoreSlim(maxConcurrency);

            var tasks = texts.Select(async text =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    return await GenerateEmbeddingAsync(text, cancellationToken);
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        private float[] GenerateOnnxEmbedding(string text)
        {
            try
            {
                var tokens = Tokenize(text);
                var inputTensor = new DenseTensor<long>(new[] { 1, tokens.Length });
                var attentionMask = new DenseTensor<long>(new[] { 1, tokens.Length });
                var tokenTypeIds = new DenseTensor<long>(new[] { 1, tokens.Length });

                for (int i = 0; i < tokens.Length; i++)
                {
                    inputTensor[0, i] = tokens[i];
                    attentionMask[0, i] = 1;
                    tokenTypeIds[0, i] = 0; // All zeros for single sentence
                }

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
                    NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
                    NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
                };

                using var results = _session!.Run(inputs);
                var embeddings = results.First().AsTensor<float>();
                
                // Mean pooling: average across all tokens (sequence length dimension)
                // Output shape is [batch_size, sequence_length, hidden_size]
                var batchSize = embeddings.Dimensions[0];
                var sequenceLength = embeddings.Dimensions[1];
                var hiddenSize = embeddings.Dimensions[2];
                
                var embedding = new float[hiddenSize];
                var tokenCount = 0;
                
                // Average across all non-padding tokens
                for (int token = 0; token < sequenceLength; token++)
                {
                    if (attentionMask[0, token] == 1) // Only consider non-padding tokens
                    {
                        for (int dim = 0; dim < hiddenSize; dim++)
                        {
                            embedding[dim] += embeddings[0, token, dim];
                        }
                        tokenCount++;
                    }
                }
                
                // Divide by token count to get mean
                for (int dim = 0; dim < hiddenSize; dim++)
                {
                    embedding[dim] /= tokenCount;
                }

                return NormalizeVector(embedding);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating ONNX embedding, falling back to simple embedding");
                return GenerateFallbackEmbedding(text);
            }
        }

        /// <summary>
        /// Fallback embedding using TF-IDF like approach
        /// This works without any model files
        /// </summary>
        private float[] GenerateFallbackEmbedding(string text)
        {
            var embedding = new float[EmbeddingDimension];
            var words = Regex.Split(text.ToLower(), @"\W+")
                            .Where(w => !string.IsNullOrEmpty(w))
                            .ToList();

            // Simple hash-based embedding for demo purposes
            foreach (var word in words)
            {
                var hash = GetStableHash(word);
                for (int i = 0; i < EmbeddingDimension; i++)
                {
                    var idx = (hash + i * 31) % EmbeddingDimension;
                    embedding[idx] += 1.0f / (float)Math.Sqrt(words.Count);
                }
            }

            return NormalizeVector(embedding);
        }

        private long[] Tokenize(string text)
        {
            text = NormalizeText(text);

            // Keep Arabic letters/numbers as tokens too. \W split drops many scripts incorrectly.
            var words = Regex.Matches(text, @"[\p{L}\p{N}]+")
                             .Select(m => m.Value)
                             .Where(w => !string.IsNullOrEmpty(w))
                             .Take(MaxTokens)
                             .ToList();

            var tokens = new List<long> { 101 }; // [CLS] token

            foreach (var word in words)
            {
                // Vocab is typically lowercase for Latin scripts.
                var key = word.ToLowerInvariant();
                if (_vocabulary.TryGetValue(key, out int tokenId))
                {
                    tokens.Add(tokenId);
                }
                else
                {
                    tokens.Add(100); // [UNK] token
                }
            }

            tokens.Add(102); // [SEP] token

            // Pad to MaxTokens
            while (tokens.Count < MaxTokens)
            {
                tokens.Add(0); // [PAD] token
            }

            return tokens.Take(MaxTokens).ToArray();
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            // Normalize common Arabic forms and collapse whitespace.
            // This is intentionally lightweight (no heavy NLP deps).
            var normalized = text
                .Replace('\u0640', ' ') // tatweel
                .Replace('?', '?')
                .Replace('?', '?')
                .Replace('?', '?')
                .Replace('?', '?')
                .Replace('?', '?');

            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        private Dictionary<string, int> BuildVocabulary()
        {
            var vocabPath = Path.Combine(Directory.GetCurrentDirectory(), "Models", "vocab.txt");
            
            if (File.Exists(vocabPath))
            {
                try
                {
                    _logger.LogInformation("Loading vocabulary from {VocabPath}", vocabPath);
                    var vocab = new Dictionary<string, int>();
                    var lines = File.ReadAllLines(vocabPath);
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        vocab[lines[i]] = i;
                    }
                    
                    _logger.LogInformation("Loaded {Count} tokens from vocabulary file", vocab.Count);
                    return vocab;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load vocabulary file, using fallback");
                    return BuildFallbackVocabulary();
                }
            }
            else
            {
                _logger.LogWarning("Vocabulary file not found at {VocabPath}. Using simplified fallback vocabulary.", vocabPath);
                _logger.LogInformation("For proper tokenization, download vocab.txt from all-MiniLM-L6-v2 model");
                return BuildFallbackVocabulary();
            }
        }

        private Dictionary<string, int> BuildFallbackVocabulary()
        {
            // Simplified vocabulary for demo purposes when vocab.txt is not available
            var vocab = new Dictionary<string, int>
            {
                ["[PAD]"] = 0,
                ["[UNK]"] = 100,
                ["[CLS]"] = 101,
                ["[SEP]"] = 102
            };

            // Add common words (this is simplified - real vocab has 30k+ words)
            var commonWords = new[] { "the", "a", "an", "is", "are", "was", "were", "be", "been", 
                "have", "has", "had", "do", "does", "did", "will", "would", "could", "should",
                "can", "may", "might", "must", "to", "from", "in", "on", "at", "by", "for", 
                "with", "about", "as", "into", "through", "during", "before", "after", "above",
                "below", "up", "down", "out", "over", "under", "again", "further", "then", "once" };

            for (int i = 0; i < commonWords.Length; i++)
            {
                vocab[commonWords[i]] = 1000 + i;
            }

            return vocab;
        }

        private float[] NormalizeVector(float[] vector)
        {
            var magnitude = (float)Math.Sqrt(vector.Sum(v => v * v));
            if (magnitude > 0)
            {
                for (int i = 0; i < vector.Length; i++)
                {
                    vector[i] /= magnitude;
                }
            }
            return vector;
        }

        private int GetStableHash(string str)
        {
            unchecked
            {
                int hash1 = 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1 || str[i + 1] == '\0')
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return Math.Abs(hash1 + (hash2 * 1566083941));
            }
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}
