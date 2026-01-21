using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RAGDemoBackend.Models;
using RAGDemoBackend.Services;

namespace RAGDemoBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("chat")]
    public class RagController : ControllerBase
    {
        private readonly IDocumentService _documentService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RagController> _logger;

        public RagController(IDocumentService documentService, IConfiguration configuration, ILogger<RagController> logger)
        {
            _documentService = documentService;
            _configuration = configuration;
            _logger = logger;
        }

        //[Authorize(Policy = "Admin")]
        [HttpPost("upload")]
        public async Task<ActionResult> UploadDocument(IFormFile file, CancellationToken cancellationToken)
        {
            // Debug: Log user identity and claims
            var userName = User?.Identity?.Name ?? User?.FindFirst("sub")?.Value;
            var roles = User?.Claims.Where(c => c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
            var claims = User?.Claims.Select(c => new { c.Type, c.Value }).ToList();
            _logger.LogDebug("[DEBUG] UploadDocument - User: {User}, Roles: {Roles}, Claims: {Claims}", userName, roles, claims);
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            var maxBytes = _configuration.GetValue<long>("Uploads:MaxBytes", 10 * 1024 * 1024); // 10 MB
            if (file.Length > maxBytes)
            {
                return BadRequest($"File too large. Max allowed size is {maxBytes} bytes.");
            }

            var allowedContentTypes = _configuration.GetSection("Uploads:AllowedContentTypes").Get<string[]>()
                                     ?? new[] { "application/pdf" };
            if (!allowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest($"Unsupported content type '{file.ContentType}'.");
            }

            var allowedExtensions = _configuration.GetSection("Uploads:AllowedExtensions").Get<string[]>()
                                    ?? new[] { ".pdf" };
            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest("Unsupported file extension.");
            }

            // Store uploads in an app-controlled temp folder (avoid sharing global temp paths)
            var uploadsTempPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "TempUploads");
            Directory.CreateDirectory(uploadsTempPath);

            var tempPath = Path.Combine(uploadsTempPath, $"{Guid.NewGuid():n}{extension}");

            try
            {
                await using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await file.CopyToAsync(stream, cancellationToken);
                }

                // Malware scanning integration point.
                // In production, scan `tempPath` via an AV engine/service (e.g., ClamAV) before processing.

                var chunks = await _documentService.ProcessPDF(tempPath, cancellationToken);

                return Ok(new { message = $"Processed {file.FileName}", chunksCreated = chunks.Count });
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(tempPath))
                    {
                        System.IO.File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Best-effort cleanup; do not mask the original exception.
                }
            }
        }

        //[Authorize(Policy = "Admin")]
        [HttpPost("ingest-url")]
        public async Task<ActionResult<WebContentResponse>> IngestWebsite([FromBody] WebContentRequest request, CancellationToken cancellationToken)
        {
            // Debug: Log user identity and claims
            var userName = User?.Identity?.Name ?? User?.FindFirst("sub")?.Value;
            var roles = User?.Claims.Where(c => c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
            var claims = User?.Claims.Select(c => new { c.Type, c.Value }).ToList();
            _logger.LogDebug("[DEBUG] IngestWebsite - User: {User}, Roles: {Roles}, Claims: {Claims}", userName, roles, claims);
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

        //[Authorize(Policy = "Admin")]
        [HttpGet("stats")]
        public async Task<ActionResult> GetStats()
        {
            // Debug: Log user identity and claims
            var userName = User?.Identity?.Name ?? User?.FindFirst("sub")?.Value;
            var roles = User?.Claims.Where(c => c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
            var claims = User?.Claims.Select(c => new { c.Type, c.Value }).ToList();
            _logger.LogDebug("[DEBUG] GetStats - User: {User}, Roles: {Roles}, Claims: {Claims}", userName, roles, claims);
            var count = await _documentService.GetDocumentCount();
            var modelName = _configuration["Embeddings:ModelName"] ?? "default";
            var dimension = _configuration.GetValue<int>("Embeddings:Dimension", 384);
            return Ok(new
            {
                documentChunks = count,
                vectorStore = "Qdrant",
                embeddingModel = modelName,
                embeddingDimension = dimension,
                timestamp = DateTime.UtcNow
            });
        }

        //[Authorize(Policy = "Admin")]
        [HttpGet("sources")]
        public async Task<ActionResult> GetAllSources()
        {
            // Debug: Log user identity and claims
            var userName = User?.Identity?.Name ?? User?.FindFirst("sub")?.Value;
            var roles = User?.Claims.Where(c => c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
            var claims = User?.Claims.Select(c => new { c.Type, c.Value }).ToList();
            _logger.LogDebug("[DEBUG] GetAllSources - User: {User}, Roles: {Roles}, Claims: {Claims}", userName, roles, claims);
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

        //[Authorize(Policy = "Admin")]
        [HttpDelete("document/{documentName}")]
        public async Task<ActionResult> DeleteDocument(string documentName)
        {
            // Debug: Log user identity and claims
            var userName = User?.Identity?.Name ?? User?.FindFirst("sub")?.Value;
            var roles = User?.Claims.Where(c => c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
            var claims = User?.Claims.Select(c => new { c.Type, c.Value }).ToList();
            _logger.LogDebug("[DEBUG] DeleteDocument - User: {User}, Roles: {Roles}, Claims: {Claims}", userName, roles, claims);
            var success = await _documentService.DeleteDocument(documentName);
            if (success)
                return Ok(new { message = $"Deleted {documentName}" });
            return NotFound(new { message = $"Document {documentName} not found" });
        }

        //[Authorize(Policy = "Admin")]
        [HttpPost("admin/clear-model")]
        public async Task<ActionResult> ClearModel([FromBody] ClearModelRequest request)
        {
            // Debug: Log user identity and claims
            var userName = User?.Identity?.Name ?? User?.FindFirst("sub")?.Value;
            var roles = User?.Claims.Where(c => c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
            var claims = User?.Claims.Select(c => new { c.Type, c.Value }).ToList();
            _logger.LogDebug("[DEBUG] ClearModel - User: {User}, Roles: {Roles}, Claims: {Claims}", userName, roles, claims);
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var success = await _documentService.DeleteByModel(request.ModelName);
            if (success)
            {
                return Ok(new { message = $"Deleted vectors for model '{request.ModelName}'" });
            }

            return BadRequest(new { message = "Failed to delete vectors for the specified model" });
        }
    }
}
