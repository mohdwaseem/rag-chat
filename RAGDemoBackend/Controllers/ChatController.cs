using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RAGDemoBackend.Models;
using RAGDemoBackend.Services;

namespace RAGDemoBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("chat")]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly IDocumentService _documentService;

        public ChatController(IChatService chatService, IDocumentService documentService)
        {
            _chatService = chatService;
            _documentService = documentService;
        }

        [HttpPost("ask")]
        public async Task<ActionResult<ChatResponse>> AskQuestion([FromBody] ChatRequest request, CancellationToken cancellationToken)
        {
            var response = await _chatService.GetResponseAsync(request, cancellationToken);
            return Ok(response);
        }

        [HttpPost("upload")]
        public async Task<ActionResult> UploadDocument(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            // Save file temporarily
            var tempPath = Path.GetTempFileName();
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            // Process the PDF
            var chunks = await _documentService.ProcessPDF(tempPath, cancellationToken);

            // Clean up
            System.IO.File.Delete(tempPath);

            return Ok(new { message = $"Processed {file.FileName}", chunksCreated = chunks.Count });
        }

        [HttpPost("ingest-url")]
        public async Task<ActionResult<WebContentResponse>> IngestWebsite([FromBody] WebContentRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Url))
                return BadRequest("URL is required");

            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest("Invalid URL format. Must be a valid HTTP or HTTPS URL.");
            }

            // Validate custom routes if framework is specified
            if (!string.IsNullOrEmpty(request.FrameworkType) && 
                request.FrameworkType != "static" && 
                (request.CustomRoutes == null || !request.CustomRoutes.Any()))
            {
                return BadRequest("Custom routes are required when a framework type is specified (except for static websites).");
            }

            var chunks = await _documentService.ProcessWebsite(
                request.Url,
                request.IncludeLinks,
                Math.Min(request.MaxDepth, 3), // Cap at 3 levels for safety
                request.FrameworkType,
                request.CustomRoutes,
                cancellationToken
            );

            var response = new WebContentResponse
            {
                Url = request.Url,
                ChunksCreated = chunks.Count,
                Status = "Success",
                ProcessedUrls = chunks.Select(c => c.Metadata.GetValueOrDefault("url", request.Url))
                                     .Distinct()
                                     .ToList()
            };

            return Ok(response);
        }

        [HttpGet("health")]
        public ActionResult HealthCheck()
        {
            return Ok(new { status = "RAG Demo API is running", timestamp = DateTime.UtcNow });
        }

        [HttpGet("stats")]
        public async Task<ActionResult> GetStats()
        {
            var count = await _documentService.GetDocumentCount();
            return Ok(new { 
                documentChunks = count, 
                vectorStore = "Qdrant",
                embeddingModel = "Local (all-MiniLM-L6-v2 compatible)",
                timestamp = DateTime.UtcNow 
            });
        }

        [HttpGet("sources")]
        public async Task<ActionResult> GetAllSources()
        {
            // Simple search with empty query to get all documents
            // This is a workaround - ideally we'd have a dedicated method
            var allDocs = await _documentService.SearchDocuments("", topK: 100);
            
            var sourceStats = allDocs
                .GroupBy(d => d.Source)
                .Select(g => new 
                { 
                    source = g.Key,
                    chunkCount = g.Count(),
                    sourceType = g.First().SourceType.ToString(),
                    latestUpdate = g.Max(d => d.CreatedAt)
                })
                .OrderByDescending(s => s.latestUpdate)
                .ToList();

            return Ok(new 
            { 
                totalChunks = allDocs.Count,
                totalSources = sourceStats.Count,
                sources = sourceStats,
                timestamp = DateTime.UtcNow 
            });
        }

        [HttpDelete("document/{documentName}")]
        public async Task<ActionResult> DeleteDocument(string documentName)
        {
            var success = await _documentService.DeleteDocument(documentName);
            if (success)
                return Ok(new { message = $"Deleted {documentName}" });
            return NotFound(new { message = $"Document {documentName} not found" });
        }
    }
}
