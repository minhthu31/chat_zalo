using System.Security.Claims;
using Chat.Application.DTOs.Conversations;
using Chat.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

[ApiController]
[Route("api/conversations")]
public class ConversationsController : ControllerBase
{
    private readonly IConversationService _conversationService;
    private readonly IWebHostEnvironment _env;

    public ConversationsController(
        IConversationService conversationService,
        IWebHostEnvironment env)
    {
        _conversationService = conversationService;
        _env = env;
    }

    // -----------------------------------------------------------------------
    // Helper: resolve the calling user's ID
    // -----------------------------------------------------------------------

    private Guid? ResolveCurrentUserId()
    {
        // Primary source: JWT / Identity claim
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(sub, out var userId))
            return userId;

        // Development fallback: X-User-Id header (no auth middleware yet)
        if (_env.IsDevelopment())
        {
            var header = Request.Headers["X-User-Id"].FirstOrDefault();
            if (Guid.TryParse(header, out var devId))
                return devId;
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // POST api/conversations/direct
    // -----------------------------------------------------------------------

    [HttpPost("direct")]
    public async Task<IActionResult> CreateDirect(
        [FromBody] CreateDirectConversationRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new
            {
                code    = "VALIDATION_ERROR",
                message = "TargetUserId is required and must be a valid GUID."
            });

        if (request.TargetUserId == Guid.Empty)
            return BadRequest(new
            {
                code    = "VALIDATION_ERROR",
                message = "TargetUserId must not be an empty GUID."
            });

        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return Unauthorized(new
            {
                code    = "UNAUTHORIZED",
                message = "User identity could not be determined."
            });

        try
        {
            var dto = await _conversationService.CreateDirectAsync(
                currentUserId.Value, request.TargetUserId);

            // HTTP 201 when a new conversation was created; HTTP 200 when it already existed
            return dto.IsNewlyCreated
                ? StatusCode(201, dto)
                : Ok(dto);
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "DIRECT_CONVERSATION_NOT_ALLOWED")
        {
            return StatusCode(403, new
            {
                code    = "DIRECT_CONVERSATION_NOT_ALLOWED",
                message = "You are not allowed to create this conversation."
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                code    = "VALIDATION_ERROR",
                message = ex.Message
            });
        }
    }

    // -----------------------------------------------------------------------
    // GET api/conversations
    // -----------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return Unauthorized(new
            {
                code    = "UNAUTHORIZED",
                message = "User identity could not be determined."
            });

        var dtos = await _conversationService.GetConversationsAsync(currentUserId.Value);
        return Ok(dtos);
    }
}
