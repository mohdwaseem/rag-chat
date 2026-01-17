using Azure;
using Azure.AI.Inference;
using RAGDemoBackend.Models;

namespace RAGDemoBackend.Services
{
    public interface IChatService
    {
        Task<ChatResponse> GetResponseAsync(ChatRequest request);
        Task<ChatResponse> GetResponseAsync(ChatRequest request, CancellationToken cancellationToken);
    }
    
    public class ChatService : IChatService
    {
        private readonly IDocumentService _documentService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ChatService> _logger;
        private readonly ChatCompletionsClient? _chatClient;
        private readonly Uri? _endpoint;
        private readonly string? _modelId;

        public ChatService(
            IDocumentService documentService, 
            IConfiguration configuration,
            ILogger<ChatService> logger)
        {
            _documentService = documentService;
            _configuration = configuration;
            _logger = logger;

            var useGitHubModels = _configuration.GetValue<bool>("DemoSettings:UseGitHubModels", false);
            var githubToken = _configuration["GitHub:Token"] ?? Environment.GetEnvironmentVariable("GH_TOKEN");
            if (useGitHubModels && !string.IsNullOrWhiteSpace(githubToken))
            {
                var endpoint = _configuration["GitHub:Endpoint"] ?? "https://models.inference.ai.azure.com/";
                _endpoint = new Uri(endpoint);
                _modelId = _configuration["GitHub:ModelId"] ?? "gpt-4o-mini";
                _chatClient = new ChatCompletionsClient(_endpoint, new AzureKeyCredential(githubToken));
            }
        }

        public async Task<ChatResponse> GetResponseAsync(ChatRequest request)
        {
            return await GetResponseAsync(request, CancellationToken.None);
        }

        public async Task<ChatResponse> GetResponseAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Question))
            {
                throw new ArgumentException("Question is required", nameof(request));
            }

            // Check if this is a casual conversational message
            if (IsConversationalMessage(request.Question))
            {
                return GetConversationalResponse(request.Question, request.Language);
            }

            if (_chatClient != null)
            {
                return await GetGitHubModelsResponse(request.Question, request.Language, cancellationToken);
            }
            else
            {
                return await GetMockResponse(request.Question, request.Language);
            }
        }

        private bool IsConversationalMessage(string message)
        {
            var conversationalPhrases = new[]
            {
                "thanks", "thank you", "ty", "thx", "ok", "okay", "bye", "goodbye",
                "hi", "hello", "hey", "good morning", "good afternoon", "good evening",
                "how are you", "what's up", "nice", "great", "awesome", "cool"
            };

            var lowerMessage = message.Trim().ToLowerInvariant();
            return conversationalPhrases.Any(phrase => 
                lowerMessage == phrase || 
                lowerMessage == phrase + "!" || 
                lowerMessage == phrase + "."
            );
        }

        private ChatResponse GetConversationalResponse(string message, string? language)
        {
            var lowerMessage = message.Trim().ToLowerInvariant();
            var isArabic = language == "ar";
            
            string response = lowerMessage switch
            {
                var msg when msg.Contains("thank") || msg.Contains("thx") || msg.Contains("ty") || msg.Contains("شكر") =>
                    isArabic ? "على الرحب والسعة! لا تتردد في سؤالي عن أي شيء." 
                             : "You're welcome! Feel free to ask me anything.",
                var msg when msg.Contains("hi") || msg.Contains("hello") || msg.Contains("hey") || msg.Contains("مرحب") || msg.Contains("سلام") =>
                    isArabic ? "مرحباً! كيف يمكنني مساعدتك اليوم؟"
                             : "Hello! How can I help you today?",
                var msg when msg.Contains("bye") || msg.Contains("goodbye") || msg.Contains("وداع") =>
                    isArabic ? "وداعاً! عد في أي وقت تحتاج فيه للمساعدة."
                             : "Goodbye! Come back anytime you need help.",
                var msg when msg.Contains("how are you") || msg.Contains("كيف حالك") =>
                    isArabic ? "أنا بخير، شكراً على السؤال! أنا هنا لمساعدتك."
                             : "I'm doing great, thanks for asking! I'm here to help you.",
                _ => isArabic ? "أنا هنا للمساعدة! كيف يمكنني مساعدتك؟"
                              : "I'm here to help! How can I assist you?"
            };

            return new ChatResponse
            {
                Answer = response,
                Sources = new List<string>(),
                SessionId = Guid.NewGuid().ToString()
            };
        }

        private async Task<ChatResponse> GetMockResponse(string question, string? language)
        {
            // Mock RAG workflow for demo
            var maxResults = _configuration.GetValue<int>("DemoSettings:MaxSearchResults", 5);
            var relevantDocs = await _documentService.SearchDocuments(question, maxResults);
            var sources = relevantDocs.Select(d => d.Source).Distinct().ToList();
            var isArabic = language == "ar";

            var contextSnippet = relevantDocs.Any() 
                ? string.Join(" ... ", relevantDocs.Take(2).Select(d => d.Content.Substring(0, Math.Min(100, d.Content.Length))))
                : (isArabic ? "لم يتم العثور على مستندات ذات صلة" : "No relevant documents found");

            var answer = isArabic 
                ? $"بناءً على {sources.Count} مستند(ات)، إليك ما وجدته حول '{question}': {contextSnippet}... " +
                  "[البحث الشعاعي مدعوم بـ Qdrant. أضف رمز GitHub لتمكين الردود المدعومة بالذكاء الاصطناعي.]"
                : $"Based on {sources.Count} document(s), here's what I found about '{question}': {contextSnippet}... " +
                  "[Vector search powered by Qdrant. Add GitHub token to enable AI-powered responses.]";

            return new ChatResponse
            {
                Answer = answer,
                Sources = sources,
                SessionId = Guid.NewGuid().ToString()
            };
        }

        private async Task<ChatResponse> GetGitHubModelsResponse(string question, string? language, CancellationToken cancellationToken)
        {
            try
            {
                var normalizedLanguage = string.IsNullOrWhiteSpace(language)
                    ? "en"
                    : language.Trim().ToLowerInvariant();

                // If the terminal/codepage can't render Arabic, Serilog console/file may show mojibake.
                // This ensures we at least keep the actual Unicode string flowing through the app.
                _logger.LogInformation("Using GitHub Models for question: {Question} (Language: {Language})", question, normalizedLanguage);

                // Get relevant documents from vector search with increased results
                var maxResults = _configuration.GetValue<int>("DemoSettings:MaxSearchResults", 10);
                var useQueryExpansion = _configuration.GetValue<bool>("DemoSettings:UseQueryExpansion", true);
                
                // Fetch more results initially to ensure diversity (2-3x the target)
                var initialSearchLimit = maxResults * 3;
                
                List<DocumentChunk> relevantDocs;
                
                if (useQueryExpansion)
                {
                    // Enhanced: Search with query expansion
                    _logger.LogInformation("Using query expansion for better retrieval");
                    relevantDocs = await SearchWithQueryExpansion(question, initialSearchLimit, cancellationToken);
                }
                else
                {
                    // Standard search
                    relevantDocs = await _documentService.SearchDocuments(question, initialSearchLimit, cancellationToken);
                }
                
                // Apply source diversity to ensure balanced results from all sources
                var useDiversity = _configuration.GetValue<bool>("DemoSettings:UseSourceDiversity", true);
                if (useDiversity)
                {
                    _logger.LogInformation("Applying source diversity to balance results");
                    relevantDocs = ApplySourceDiversity(relevantDocs, maxResults);
                }
                
                var sources = relevantDocs.Select(d => d.Source).Distinct().ToList();

                // Log relevance for debugging
                _logger.LogInformation("Retrieved {Count} relevant chunks from sources: {Sources}", 
                    relevantDocs.Count, string.Join(", ", sources));
                
                // Log individual chunks for detailed debugging
                for (int i = 0; i < Math.Min(3, relevantDocs.Count); i++)
                {
                    var doc = relevantDocs[i];
                    var preview = doc.Content.Length > 100 
                        ? doc.Content.Substring(0, 100) + "..." 
                        : doc.Content;
                    _logger.LogDebug("Chunk {Index}: Source={Source}, Preview={Preview}", 
                        i + 1, doc.Source, preview);
                }

                // Build context from relevant documents
                var context = relevantDocs.Any()
                    ? string.Join("\n\n", relevantDocs.Select(d => $"Source: {d.Source}\n{d.Content}"))
                    : "No relevant documents found in the knowledge base.";

                var client = _chatClient ?? throw new InvalidOperationException("Chat client not configured");
                var modelId = _modelId ?? "gpt-4o-mini";

                // Determine response language
                var isArabic = normalizedLanguage == "ar";
                var languageInstruction = isArabic 
                    ? "\n7. CRITICAL: You MUST respond in Arabic language. All your answers must be in Arabic."
                    : "\n7. CRITICAL: You MUST respond in English language. All your answers must be in English.";

                // Enhanced system prompt to encourage using context
                var systemPrompt = relevantDocs.Any()
                    ? $@"You are a helpful AI assistant. Answer questions STRICTLY based on the provided context from the knowledge base. 

IMPORTANT RULES:
1. ONLY use information from the context provided below
2. If the context contains relevant information, provide a detailed answer
3. If the context does NOT contain relevant information, clearly state: {(isArabic ? "'ليس لدي معلومات محددة حول هذا في قاعدة المعرفة'" : "'I don't have specific information about this in the knowledge base'")}
4. DO NOT use general knowledge or make assumptions
5. Cite the source when providing information
6. Keep answers clear and well-structured{languageInstruction}

FORMATTING GUIDELINES:
- Use numbered lists (1., 2., 3.) for sequential steps or ordered information
- Use bullet points (- or *) for unordered lists
- Use **bold** for important terms or headings
- Use `code` for technical terms or file names
- Break complex answers into easy-to-read sections"
                    : $@"You are a helpful AI assistant. The knowledge base does not contain information relevant to this question. 

Politely inform the user that the specific information is not available in the knowledge base, and suggest they:
1. Rephrase their question
2. Ask about topics that might be in the knowledge base
3. Contact support for more detailed information

Do not provide general knowledge answers.{languageInstruction}";

                // Create chat messages with RAG context
                var chatMessages = new List<ChatRequestMessage>
                {
                    new ChatRequestSystemMessage(systemPrompt),
                    new ChatRequestUserMessage(
                        relevantDocs.Any()
                            ? $"Context from knowledge base:\n{context}\n\nQuestion: {question}\n\nPlease provide a clear, well-formatted answer based ONLY on the context above."
                            : $"Question: {question}\n\nThe knowledge base does not contain relevant information for this question."
                    )
                };

                var requestOptions = new ChatCompletionsOptions(chatMessages)
                {
                    Model = modelId,
                    Temperature = 0.3f,  // Lower temperature for more factual responses
                    MaxTokens = 800
                };

                // Call GitHub Models API
                var response = await client.CompleteAsync(requestOptions, cancellationToken);
                var answer = response.Value.Content ?? string.Empty;

                var tokens = response.Value.Usage?.TotalTokens;
                if (tokens is not null)
                {
                    _logger.LogInformation("GitHub Models response received. Tokens used: {Tokens}", tokens);
                }
                else
                {
                    _logger.LogInformation("GitHub Models response received.");
                }

                return new ChatResponse
                {
                    Answer = answer,
                    Sources = sources,
                    SessionId = Guid.NewGuid().ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling GitHub Models API");
                
                // Fallback to mock response
                _logger.LogWarning("Falling back to mock response");
                return await GetMockResponse(question, language);
            }
        }

        private async Task<List<DocumentChunk>> SearchWithQueryExpansion(string question, int maxResults, CancellationToken cancellationToken)
        {
            // Search with original query
            var results = await _documentService.SearchDocuments(question, maxResults, cancellationToken);
            
            // If results are good enough, return them
            if (results.Count >= maxResults / 2)
            {
                _logger.LogInformation("Original query returned {Count} results", results.Count);
                return results;
            }

            // If poor results, try query expansion
            _logger.LogInformation("Original query returned only {Count} results, trying query expansion", results.Count);
            
            // Generate query variations
            var queryVariations = GenerateQueryVariations(question);
            
            var allResults = new List<DocumentChunk>(results);
            
            foreach (var variation in queryVariations)
            {
                var variationResults = await _documentService.SearchDocuments(variation, maxResults / 2, cancellationToken);
                allResults.AddRange(variationResults);
            }

            // Remove duplicates and take top results
            var uniqueResults = allResults
                .GroupBy(d => d.Id)
                .Select(g => g.First())
                .Take(maxResults)
                .ToList();

            _logger.LogInformation("Query expansion returned {Count} unique results", uniqueResults.Count);
            
            return uniqueResults;
        }

        private List<string> GenerateQueryVariations(string question)
        {
            var variations = new List<string>();
            
            // Remove question words
            var withoutQuestionWords = question
                .Replace("what is", "")
                .Replace("what are", "")
                .Replace("how do", "")
                .Replace("how can", "")
                .Replace("tell me about", "")
                .Replace("explain", "")
                .Trim();
            
            if (!string.IsNullOrWhiteSpace(withoutQuestionWords) && withoutQuestionWords != question)
            {
                variations.Add(withoutQuestionWords);
            }

            // Add key terms
            var words = question.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var keyWords = words.Where(w => w.Length > 3).ToList();
            
            if (keyWords.Any())
            {
                variations.Add(string.Join(" ", keyWords));
            }

            // Add singular/plural variations for common terms
            if (question.Contains("services"))
            {
                variations.Add(question.Replace("services", "service"));
            }
            else if (question.Contains("service"))
            {
                variations.Add(question.Replace("service", "services"));
            }

            return variations.Distinct().Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        }

        private List<DocumentChunk> ApplySourceDiversity(List<DocumentChunk> chunks, int maxResults)
        {
            if (!chunks.Any())
                return chunks;

            // Group chunks by source
            var chunksBySource = chunks
                .GroupBy(c => c.Source)
                .OrderByDescending(g => g.Count())
                .ToList();

            _logger.LogInformation("Found chunks from {SourceCount} sources: {Sources}",
                chunksBySource.Count,
                string.Join(", ", chunksBySource.Select(g => $"{g.Key} ({g.Count()})")));

            // If only one source, return as-is
            if (chunksBySource.Count == 1)
            {
                return chunks.Take(maxResults).ToList();
            }

            // Ensure diversity: Take chunks from each source in round-robin fashion
            var diverseResults = new List<DocumentChunk>();
            var sourceEnumerators = chunksBySource
                .Select(g => g.GetEnumerator())
                .ToList();

            // Round-robin selection from each source
            var activeEnumerators = sourceEnumerators.Where(e => e.MoveNext()).ToList();
            
            while (diverseResults.Count < maxResults && activeEnumerators.Any())
            {
                foreach (var enumerator in activeEnumerators.ToList())
                {
                    if (diverseResults.Count >= maxResults)
                        break;

                    diverseResults.Add(enumerator.Current);

                    if (!enumerator.MoveNext())
                    {
                        activeEnumerators.Remove(enumerator);
                    }
                }
            }

            _logger.LogInformation("Applied source diversity: {Count} chunks from {SourceCount} sources",
                diverseResults.Count,
                diverseResults.Select(c => c.Source).Distinct().Count());

            // Log breakdown
            var diversityBreakdown = diverseResults
                .GroupBy(c => c.Source)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();
            _logger.LogInformation("Diversity breakdown: {Breakdown}", string.Join(", ", diversityBreakdown));

            return diverseResults;
        }

    }
}
