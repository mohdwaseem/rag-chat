using RAGDemoBackend.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace RAGDemoBackend.Services
{
    public interface IDocumentService
    {
        Task<List<DocumentChunk>> ProcessPDF(string filePath);
        Task<List<DocumentChunk>> ProcessPDF(string filePath, CancellationToken cancellationToken);
        Task<List<DocumentChunk>> ProcessWebsite(
            string url, 
            bool includeLinks = false, 
            int maxDepth = 1, 
            string? frameworkType = null, 
            List<string>? customRoutes = null);
        Task<List<DocumentChunk>> ProcessWebsite(
            string url,
            bool includeLinks,
            int maxDepth,
            string? frameworkType,
            List<string>? customRoutes,
            CancellationToken cancellationToken);
        Task<List<DocumentChunk>> SearchDocuments(string query, int topK = 5);
        Task<List<DocumentChunk>> SearchDocuments(string query, int topK, CancellationToken cancellationToken);
        Task<List<DocumentChunk>> SearchDocuments(string query, int topK, string? language, CancellationToken cancellationToken);
        Task<bool> DeleteDocument(string documentSource);
        Task<int> GetDocumentCount();
    }

    // Services/DocumentService.cs
    public class DocumentService : IDocumentService
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorStoreService _vectorStore;
        private readonly ILogger<DocumentService> _logger;
        private readonly IConfiguration _configuration;

        public DocumentService(
            IEmbeddingService embeddingService,
            IVectorStoreService vectorStore,
            ILogger<DocumentService> logger,
            IConfiguration configuration)
        {
            _embeddingService = embeddingService;
            _vectorStore = vectorStore;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<List<DocumentChunk>> ProcessPDF(string filePath)
        {
            return await ProcessPDF(filePath, CancellationToken.None);
        }

        public async Task<List<DocumentChunk>> ProcessPDF(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Processing PDF: {FilePath}", filePath);

                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    throw new ArgumentException("File not found", nameof(filePath));
                }
                
                // Extract text from PDF
                var text = await ExtractTextFromPDFAsync(filePath, cancellationToken);
                
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("No text extracted from PDF: {FilePath}", filePath);
                    return new List<DocumentChunk>();
                }

                // Get chunk size from configuration
                var chunkSize = _configuration.GetValue<int>("DemoSettings:ChunkSize", 500);
                
                // Split into chunks
                var chunks = SplitIntoChunks(text, chunkSize, filePath);
                _logger.LogInformation("Created {Count} chunks from PDF", chunks.Count);

                // Generate embeddings for all chunks
                var texts = chunks.Select(c => c.Content).ToList();
                var embeddings = await _embeddingService.GenerateEmbeddingsAsync(texts, cancellationToken);
                _logger.LogInformation("Generated embeddings for {Count} chunks", embeddings.Count);

                // Store in Qdrant
                var success = await _vectorStore.UpsertDocumentChunksAsync(chunks, embeddings);
                
                if (success)
                {
                    _logger.LogInformation("Successfully stored {Count} chunks in Qdrant", chunks.Count);
                }
                else
                {
                    _logger.LogError("Failed to store chunks in Qdrant");
                }

                return chunks;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF: {FilePath}", filePath);
                throw;
            }
        }

        private async Task<string> ExtractTextFromPDFAsync(string filePath, CancellationToken cancellationToken)
        {
            var text = new StringBuilder();

            try
            {
                // CORRECT iText7 usage
                using var pdfReader = new iText.Kernel.Pdf.PdfReader(filePath);
                using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(pdfReader);

                var strategy = new iText.Kernel.Pdf.Canvas.Parser.Listener.LocationTextExtractionStrategy();

                for (int page = 1; page <= pdfDoc.GetNumberOfPages(); page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pageText = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(
                        pdfDoc.GetPage(page), strategy);
                    text.AppendLine(pageText);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDF extraction error for {FilePath}", filePath);
                return string.Empty;
            }

            return text.ToString();
        }

        private List<DocumentChunk> SplitIntoChunks(string text, int chunkSize, string sourceFilePath)
        {
            var chunks = new List<DocumentChunk>();
            var fileName = Path.GetFileName(sourceFilePath);

            for (int i = 0; i < text.Length; i += chunkSize)
            {
                var chunk = text.Substring(i, Math.Min(chunkSize, text.Length - i));
                chunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    Content = chunk,
                    Source = fileName,
                    Index = i / chunkSize,
                    SourceType = SourceType.PDF,
                    FilePath = sourceFilePath,
                    CreatedAt = DateTime.UtcNow,
                    Metadata = new Dictionary<string, string>
                    {
                        { "language", DetectLanguage(chunk) }
                    }
                });
            }

            return chunks;
        }

        public async Task<List<DocumentChunk>> SearchDocuments(string query, int topK = 5)
        {
            return await SearchDocuments(query, topK, CancellationToken.None);
        }

        public async Task<List<DocumentChunk>> SearchDocuments(string query, int topK, CancellationToken cancellationToken)
        {
            return await SearchDocuments(query, topK, null, cancellationToken);
        }

        public async Task<List<DocumentChunk>> SearchDocuments(string query, int topK, string? language, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Searching for: {Query}", query);

                if (string.IsNullOrWhiteSpace(query))
                {
                    return new List<DocumentChunk>();
                }

                // Generate embedding for the query
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

                // Search in Qdrant (optionally filter by language)
                var results = await _vectorStore.SearchSimilarChunksAsync(queryEmbedding, topK, language);

                _logger.LogInformation("Found {Count} results for query", results.Count);

                return results.Select(r => r.Chunk).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching documents");
                return new List<DocumentChunk>();
            }
        }

        public async Task<bool> DeleteDocument(string documentSource)
        {
            return await _vectorStore.DeleteDocumentAsync(documentSource);
        }

        public async Task<int> GetDocumentCount()
        {
            return await _vectorStore.GetDocumentCountAsync();
        }

        public async Task<List<DocumentChunk>> ProcessWebsite(
            string url, 
            bool includeLinks = false, 
            int maxDepth = 1, 
            string? frameworkType = null, 
            List<string>? customRoutes = null)
        {
            return await ProcessWebsite(url, includeLinks, maxDepth, frameworkType, customRoutes, CancellationToken.None);
        }

        public async Task<List<DocumentChunk>> ProcessWebsite(
            string url,
            bool includeLinks,
            int maxDepth,
            string? frameworkType,
            List<string>? customRoutes,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Processing website: {Url} (Framework: {Framework}, Custom Routes: {HasRoutes})", 
                    url, frameworkType ?? "none", customRoutes?.Any() == true ? "yes" : "no");

                if (!IsValidUrl(url))
                {
                    _logger.LogWarning("Invalid URL provided: {Url}", url);
                    throw new ArgumentException("Invalid URL format", nameof(url));
                }

                var processedUrls = new HashSet<string>();
                var allChunks = new List<DocumentChunk>();

                await ProcessUrlRecursive(url, maxDepth, 0, processedUrls, allChunks, includeLinks, frameworkType, customRoutes, cancellationToken);

                if (allChunks.Count == 0)
                {
                    _logger.LogWarning("No content extracted from website: {Url}", url);
                    return allChunks;
                }

                // Generate embeddings for all chunks
                var texts = allChunks.Select(c => c.Content).ToList();
                var embeddings = await _embeddingService.GenerateEmbeddingsAsync(texts, cancellationToken);
                _logger.LogInformation("Generated embeddings for {Count} chunks from website", embeddings.Count);

                // Store in Qdrant
                var success = await _vectorStore.UpsertDocumentChunksAsync(allChunks, embeddings);

                if (success)
                {
                    _logger.LogInformation("Successfully stored {Count} chunks from website in Qdrant", allChunks.Count);
                }
                else
                {
                    _logger.LogError("Failed to store website chunks in Qdrant");
                }

                return allChunks;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing website: {Url}", url);
                throw;
            }
        }

        private async Task ProcessUrlRecursive(
            string url, 
            int maxDepth, 
            int currentDepth, 
            HashSet<string> processedUrls, 
            List<DocumentChunk> allChunks, 
            bool includeLinks,
            string? frameworkType,
            List<string>? customRoutes,
            CancellationToken cancellationToken)
        {
            if (currentDepth >= maxDepth || processedUrls.Contains(url))
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            processedUrls.Add(url);

            try
            {
                var (content, links) = await ExtractWebContentAsync(url, frameworkType, customRoutes);

                if (!string.IsNullOrWhiteSpace(content))
                {
                    var chunkSize = _configuration.GetValue<int>("DemoSettings:ChunkSize", 500);
                    var chunks = SplitIntoWebChunks(content, chunkSize, url);
                    allChunks.AddRange(chunks);
                    _logger.LogInformation("Extracted {Count} chunks from {Url}", chunks.Count, url);
                }

                // Process linked pages if enabled
                if (includeLinks && currentDepth + 1 < maxDepth && links.Any())
                {
                    var baseUri = new Uri(url);
                    var sameDomainLinks = links
                        .Where(link => IsValidUrl(link))
                        .Select(link => new Uri(baseUri, link).AbsoluteUri)
                        .Where(absoluteUrl => new Uri(absoluteUrl).Host == baseUri.Host)
                        .Take(5); // Limit to 5 links per page to avoid excessive scraping

                    foreach (var link in sameDomainLinks)
                    {
                        await ProcessUrlRecursive(link, maxDepth, currentDepth + 1, processedUrls, allChunks, includeLinks, frameworkType, customRoutes, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process URL: {Url}", url);
            }
        }

        private async Task<(string Content, List<string> Links)> ExtractWebContentAsync(
            string url, 
            string? frameworkType = null, 
            List<string>? customRoutes = null)
        {
            var useHeadlessBrowser = _configuration.GetValue<bool>("DemoSettings:UseHeadlessBrowser", false);
            
            // Use headless browser for SPA frameworks or if explicitly configured
            var shouldUseHeadless = useHeadlessBrowser || 
                (!string.IsNullOrEmpty(frameworkType) && frameworkType != "static");
            
            if (shouldUseHeadless)
            {
                return await ExtractWebContentWithPuppeteerAsync(url, frameworkType, customRoutes);
            }
            else
            {
                return await ExtractWebContentWithHtmlAgilityPackAsync(url);
            }
        }

        private async Task<(string Content, List<string> Links)> ExtractWebContentWithPuppeteerAsync(
            string url,
            string? frameworkType = null,
            List<string>? customRoutes = null)
        {
            try
            {
                _logger.LogInformation("Using Puppeteer (headless browser) to fetch: {Url}", url);

                // Use custom routes if provided, otherwise fall back to config, then default to root
                var routesToScrape = customRoutes?.Any() == true 
                    ? customRoutes.ToArray() 
                    : _configuration.GetSection("DemoSettings:AngularRoutes").Get<string[]>() 
                      ?? new[] { "/" };
                
                _logger.LogInformation("Will scrape {Count} routes", routesToScrape.Length);
                _logger.LogInformation("Routes: {Routes}", string.Join(", ", routesToScrape));

                // Download browser if not already present
                var browserFetcher = new PuppeteerSharp.BrowserFetcher();
                await browserFetcher.DownloadAsync();

                var launchOptions = new PuppeteerSharp.LaunchOptions
                {
                    Headless = true,
                    Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--lang=en-US,ar-SA" }
                };

                await using var browser = await PuppeteerSharp.Puppeteer.LaunchAsync(launchOptions);
                
                var allContent = new StringBuilder();
                var allLinks = new List<string>();

                // Get base URL (without path)
                var baseUri = new Uri(url);
                var baseUrl = $"{baseUri.Scheme}://{baseUri.Host}";

                // Scrape both language versions
                var languages = new[] { "ar", "en" }; // Arabic (default) and English
                
                foreach (var language in languages)
                {
                    var languageName = language == "ar" ? "Arabic" : "English";
                    _logger.LogInformation("=== Scraping {Language} Version ===", languageName);
                    
                    foreach (var route in routesToScrape)
                    {
                        var fullUrl = baseUrl + route;
                        _logger.LogInformation("Scraping {Language} version of {Route}", languageName, fullUrl);
                        
                        await using var page = await browser.NewPageAsync();

                        // Set user agent
                        await page.SetUserAgentAsync("RAG-Demo-Bot/1.0");

                        // Set timeout
                        var timeout = _configuration.GetValue<int>("DemoSettings:BrowserTimeout", 30000);
                        page.DefaultNavigationTimeout = timeout;

                        try
                        {
                            // Navigate to URL first
                            await page.GoToAsync(fullUrl, new PuppeteerSharp.NavigationOptions
                            {
                                WaitUntil = new[] { PuppeteerSharp.WaitUntilNavigation.Networkidle2 }
                            });

                            // Set localStorage for language AFTER page loads
                            await page.EvaluateExpressionAsync($@"
                                localStorage.setItem('dhamen-language', '{language}');
                            ");

                            _logger.LogInformation("Set localStorage dhamen-language={Language}, reloading {Route}...", language, route);

                            // Reload page with the new language setting
                            await page.ReloadAsync(new PuppeteerSharp.NavigationOptions
                            {
                                WaitUntil = new[] { PuppeteerSharp.WaitUntilNavigation.Networkidle2 }
                            });

                            // Wait longer for Angular to fully render with the selected language
                            var angularWaitTime = _configuration.GetValue<int>("DemoSettings:AngularWaitTime", 5000);
                            _logger.LogInformation("Waiting {Time}ms for Angular to render {Language} content on {Route}...", 
                                angularWaitTime, languageName, route);
                            await Task.Delay(angularWaitTime);

                            // Optional: Wait for a specific element that indicates content is loaded
                            try
                            {
                                await page.WaitForSelectorAsync("body", new PuppeteerSharp.WaitForSelectorOptions
                                {
                                    Timeout = 5000
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning("Could not wait for body selector on {Route}: {Message}", route, ex.Message);
                            }

                            // Get the rendered HTML
                            var html = await page.GetContentAsync();

                            _logger.LogInformation("Successfully fetched rendered {Language} HTML from {Route}", languageName, route);

                            // Now use HtmlAgilityPack to parse the rendered HTML
                            var doc = new HtmlAgilityPack.HtmlDocument();
                            doc.LoadHtml(html);

                            // Remove script and style elements
                            doc.DocumentNode.Descendants()
                                .Where(n => n.Name == "script" || n.Name == "style")
                                .ToList()
                                .ForEach(n => n.Remove());

                            // Extract main content
                            var content = new StringBuilder();
                            
                            // Try to find main content areas first
                            var mainContent = doc.DocumentNode.SelectSingleNode("//main") 
                                ?? doc.DocumentNode.SelectSingleNode("//article")
                                ?? doc.DocumentNode.SelectSingleNode("//div[@class='content']")
                                ?? doc.DocumentNode.SelectSingleNode("//div[@id='content']")
                                ?? doc.DocumentNode.SelectSingleNode("//body");

                            if (mainContent != null)
                            {
                                // Extract text from paragraphs, headings, and list items
                                var textNodes = mainContent.Descendants()
                                    .Where(n => n.Name == "p" || n.Name == "h1" || n.Name == "h2" || 
                                               n.Name == "h3" || n.Name == "h4" || n.Name == "h5" || 
                                               n.Name == "h6" || n.Name == "li" || n.Name == "td")
                                    .Select(n => CleanText(n.InnerText))
                                    .Where(text => !string.IsNullOrWhiteSpace(text));

                                foreach (var text in textNodes)
                                {
                                    content.AppendLine(text);
                                }

                                // Debug logging
                                _logger.LogInformation("{Language} extraction from {Route} - Main content node: {NodeName}, Total descendants: {Count}", 
                                    languageName, route, mainContent.Name, mainContent.Descendants().Count());
                                _logger.LogInformation("Extracted {Language} text from {Route}: {Length} characters", 
                                    languageName, route, content.Length);

                                // Fallback: if no content from semantic tags, try extracting from all divs
                                if (content.Length < 100)
                                {
                                    _logger.LogWarning("Very little {Language} content found on {Route} with semantic tags, trying broader extraction", 
                                        languageName, route);
                                    content.Clear();
                                    
                                    var allTextNodes = mainContent.Descendants()
                                        .Where(n => n.Name == "p" || n.Name == "h1" || n.Name == "h2" || 
                                                   n.Name == "h3" || n.Name == "h4" || n.Name == "h5" || 
                                                   n.Name == "h6" || n.Name == "li" || n.Name == "td" ||
                                                   n.Name == "div" || n.Name == "span")
                                        .Where(n => !string.IsNullOrWhiteSpace(n.InnerText))
                                        .Select(n => CleanText(n.InnerText))
                                        .Where(text => !string.IsNullOrWhiteSpace(text) && text.Length > 10)
                                        .Distinct();

                                    foreach (var text in allTextNodes)
                                    {
                                        content.AppendLine(text);
                                    }
                                    
                                    _logger.LogInformation("Broader extraction on {Route} yielded {Language}: {Length} characters", 
                                        route, languageName, content.Length);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("No main content node found in {Language} HTML structure on {Route}", 
                                    languageName, route);
                            }

                            // Add route and language prefix to content to distinguish versions
                            if (content.Length > 0)
                            {
                                allContent.AppendLine($"[{languageName} Version - Route: {route}]");
                                allContent.AppendLine(content.ToString());
                                allContent.AppendLine(); // Add separation between routes/languages
                            }

                            // Extract links (only from first language and first route to avoid duplicates)
                            if (language == "ar" && route == routesToScrape[0])
                            {
                                var links = doc.DocumentNode.Descendants("a")
                                    .Select(a => a.GetAttributeValue("href", ""))
                                    .Where(href => !string.IsNullOrWhiteSpace(href))
                                    .ToList();
                                
                                allLinks.AddRange(links);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error scraping {Language} version of {Route}", languageName, route);
                        }
                        finally
                        {
                            await page.CloseAsync();
                        }
                    }
                }

                _logger.LogInformation("=== Scraping Complete ===");
                _logger.LogInformation("Combined extraction from {LanguageCount} languages x {RouteCount} routes: {Length} total characters", 
                    languages.Length, routesToScrape.Length, allContent.Length);

                return (allContent.ToString(), allLinks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting content with Puppeteer from URL: {Url}", url);
                return (string.Empty, new List<string>());
            }
        }

        private async Task<(string Content, List<string> Links)> ExtractWebContentWithHtmlAgilityPackAsync(string url)
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "RAG-Demo-Bot/1.0");

            try
            {
                var html = await httpClient.GetStringAsync(url);
                
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);

                // Remove script and style elements
                doc.DocumentNode.Descendants()
                    .Where(n => n.Name == "script" || n.Name == "style")
                    .ToList()
                    .ForEach(n => n.Remove());

                // Extract main content
                var content = new StringBuilder();
                
                // Try to find main content areas first
                var mainContent = doc.DocumentNode.SelectSingleNode("//main") 
                    ?? doc.DocumentNode.SelectSingleNode("//article")
                    ?? doc.DocumentNode.SelectSingleNode("//div[@class='content']")
                    ?? doc.DocumentNode.SelectSingleNode("//div[@id='content']")
                    ?? doc.DocumentNode.SelectSingleNode("//body");

                if (mainContent != null)
                {
                    // Extract text from paragraphs, headings, and list items
                    var textNodes = mainContent.Descendants()
                        .Where(n => n.Name == "p" || n.Name == "h1" || n.Name == "h2" || 
                                   n.Name == "h3" || n.Name == "h4" || n.Name == "h5" || 
                                   n.Name == "h6" || n.Name == "li" || n.Name == "td")
                        .Select(n => CleanText(n.InnerText))
                        .Where(text => !string.IsNullOrWhiteSpace(text));

                    foreach (var text in textNodes)
                    {
                        content.AppendLine(text);
                    }

                    // Debug logging
                    _logger.LogInformation("HTML extraction - Main content node: {NodeName}, Total descendants: {Count}", 
                        mainContent.Name, mainContent.Descendants().Count());
                    _logger.LogInformation("Extracted text length: {Length} characters", content.Length);

                    // Fallback: if no content from semantic tags, try extracting from all divs
                    if (content.Length < 100)
                    {
                        _logger.LogWarning("Very little content found with semantic tags, trying broader extraction");
                        content.Clear();
                        
                        var allTextNodes = mainContent.Descendants()
                            .Where(n => n.Name == "p" || n.Name == "h1" || n.Name == "h2" || 
                                       n.Name == "h3" || n.Name == "h4" || n.Name == "h5" || 
                                       n.Name == "h6" || n.Name == "li" || n.Name == "td" ||
                                       n.Name == "div" || n.Name == "span")
                            .Where(n => !string.IsNullOrWhiteSpace(n.InnerText))
                            .Select(n => CleanText(n.InnerText))
                            .Where(text => !string.IsNullOrWhiteSpace(text) && text.Length > 10)
                            .Distinct();

                        foreach (var text in allTextNodes)
                        {
                            content.AppendLine(text);
                        }
                        
                        _logger.LogInformation("Broader extraction yielded: {Length} characters", content.Length);
                    }
                }
                else
                {
                    _logger.LogWarning("No main content node found in HTML structure");
                }

                // Extract links
                var links = doc.DocumentNode.Descendants("a")
                    .Select(a => a.GetAttributeValue("href", ""))
                    .Where(href => !string.IsNullOrWhiteSpace(href))
                    .ToList();

                return (content.ToString(), links);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error fetching URL: {Url}", url);
                return (string.Empty, new List<string>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting content from URL: {Url}", url);
                return (string.Empty, new List<string>());
            }
        }

        private string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Decode HTML entities
            text = System.Net.WebUtility.HtmlDecode(text);

            // Replace multiple whitespaces with single space
            text = Regex.Replace(text, @"\s+", " ");

            return text.Trim();
        }

        private bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        private List<DocumentChunk> SplitIntoWebChunks(string text, int chunkSize, string sourceUrl)
        {
            var chunks = new List<DocumentChunk>();
            var uri = new Uri(sourceUrl);
            var displayName = $"{uri.Host}{uri.PathAndQuery}";

            _logger.LogInformation("Chunking web content - Total text length: {Length}, Chunk size: {ChunkSize}", 
                text.Length, chunkSize);

            var skippedCount = 0;
            for (int i = 0; i < text.Length; i += chunkSize)
            {
                var chunk = text.Substring(i, Math.Min(chunkSize, text.Length - i));
                
                // Skip chunks that are too small or just whitespace
                if (chunk.Length < 50 || string.IsNullOrWhiteSpace(chunk))
                {
                    skippedCount++;
                    continue;
                }

                chunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    Content = chunk,
                    Source = displayName,
                    Index = i / chunkSize,
                    SourceType = SourceType.Website,
                    CreatedAt = DateTime.UtcNow,
                    Metadata = new Dictionary<string, string>
                    {
                        { "url", sourceUrl },
                        { "domain", uri.Host },
                        { "scrapedAt", DateTime.UtcNow.ToString("o") },
                        { "language", DetectLanguage(chunk) }
                    }
                });
            }

            if (skippedCount > 0)
            {
                _logger.LogInformation("Skipped {Count} chunks (too small or empty)", skippedCount);
            }

            return chunks;
        }

        private string DetectLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "en";
            }

            var arabicCount = text.Count(c =>
                (c >= '\u0600' && c <= '\u06FF') ||
                (c >= '\u0750' && c <= '\u077F') ||
                (c >= '\u08A0' && c <= '\u08FF'));

            return arabicCount > 0 ? "ar" : "en";
        }
    }
}
