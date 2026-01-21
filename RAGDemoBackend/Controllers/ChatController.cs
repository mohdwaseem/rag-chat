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

        public ChatController(IChatService chatService)
        {
            _chatService = chatService;
        }

        [HttpPost("ask")]
        public async Task<ActionResult<ChatResponse>> AskQuestion([FromBody] ChatRequest request, CancellationToken cancellationToken)
        {
            var response = await _chatService.GetResponseAsync(request, cancellationToken);
            return Ok(response);
        }
    }
}
