using System.Security.Claims;
using Chat.Api.Filters;
using Chat.Api.Hubs;
using Chat.Application.DTOs.Auth;
using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/conversations/{id:guid}/messages")]
public sealed class MessagesController(IMessageService messages, ConversationNotifications notifications,
    ILogger<MessagesController> logger) : ControllerBase
{
    [HttpPost]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> Send(Guid id, SendMessageRequest request)
    {
        if (!TryUser(out var userId)) return Unauthorized(new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Unauthorized" });
        try
        {
            var result = await messages.SendAsync(userId, id, request);
            if (result.IsNewlyCreated)
            {
                // SendAsync has committed AND disposed its transaction before returning.
                // A notification failure must not turn a committed send into an HTTP failure.
                try { await notifications.PublishAsync(id); }
                catch (Exception ex) { logger.LogError(ex, "Message committed but notification failed for {ConversationId}", id); }
            }
            return StatusCode(result.IsNewlyCreated ? 201 : 200, result);
        }
        catch (MessageException ex) { return Error(ex); }
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid id, long? beforeSeq, long? afterSeq, int limit = 20)
    {
        if (!TryUser(out var userId)) return Unauthorized(new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Unauthorized" });
        if (beforeSeq.HasValue && afterSeq.HasValue)
            return BadRequest(new AuthErrorResponse { Code = "INVALID_PAGINATION_PARAMETERS", Message = "Use only beforeSeq or afterSeq." });
        try
        {
            return Ok(afterSeq.HasValue
                ? await messages.GetNewMessagesAsync(userId, id, afterSeq.Value, limit)
                : await messages.GetHistoryAsync(userId, id, beforeSeq, limit));
        }
        catch (MessageException ex) { return Error(ex); }
    }

    private bool TryUser(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id) && id != Guid.Empty;
    private ObjectResult Error(MessageException ex) => StatusCode(ex.StatusCode, new AuthErrorResponse { Code = ex.Code, Message = ex.Message });
}
