using System.Security.Claims;
using Chat.Api.Filters;
using Chat.Application.DTOs.Auth;
using Chat.Application.DTOs.Conversations;
using Chat.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/conversations")]
public class ConversationsController : ControllerBase
{
    private readonly IConversationService _conversationService;

    public ConversationsController(IConversationService conversationService)
    {
        _conversationService = conversationService;
    }

    // -----------------------------------------------------------------------
    // Helper: resolve the authenticated user's ID
    // -----------------------------------------------------------------------

    private Guid? ResolveCurrentUserId()
    {
        if (User.Identity?.IsAuthenticated != true)
            return null;

        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(sub, out var userId))
            return userId;

        return null;
    }

    // -----------------------------------------------------------------------
    // POST api/conversations/direct
    // -----------------------------------------------------------------------

    [HttpPost("direct")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> CreateDirect(
        [FromBody] CreateDirectConversationRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse
            {
                Code    = "VALIDATION_ERROR",
                Message = "TargetUserId is required and must be a valid GUID."
            });

        if (request.TargetUserId == Guid.Empty)
            return BadRequest(new AuthErrorResponse
            {
                Code    = "VALIDATION_ERROR",
                Message = "TargetUserId must not be an empty GUID."
            });

        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return StatusCode(StatusCodes.Status401Unauthorized, new AuthErrorResponse
            {
                Code    = "UNAUTHORIZED",
                Message = "Unauthorized"
            });

        try
        {
            var dto = await _conversationService.CreateDirectAsync(
                currentUserId.Value, request.TargetUserId);

            return dto.IsNewlyCreated
                ? StatusCode(StatusCodes.Status201Created, dto)
                : Ok(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                code = "NOT_FOUND",
                message = "Target user does not exist."
            });
        }
        catch (UnauthorizedAccessException ex) when (ex.Message is "DIRECT_CONVERSATION_NOT_ALLOWED" or "CONVERSATION_ACCESS_DENIED")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new AuthErrorResponse
            {
                Code    = ex.Message,
                Message = "You are not allowed to create this conversation."
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new AuthErrorResponse
            {
                Code    = "VALIDATION_ERROR",
                Message = ex.Message
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
            return StatusCode(StatusCodes.Status401Unauthorized, new AuthErrorResponse
            {
                Code    = "UNAUTHORIZED",
                Message = "Unauthorized"
            });

        var dtos = await _conversationService.GetConversationsAsync(currentUserId.Value);
        return Ok(dtos);
    }
}
