<<<<<<< HEAD
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Chat.Api.Filters;
using Chat.Application.DTOs.Messages;
using Chat.Application.Services;
using Chat.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/conversations/{id}/messages")]
public sealed class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;

    public MessagesController(IMessageService messageService)
    {
        _messageService = messageService;
    }

    [HttpPost]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> SendMessage(
        [FromRoute] Guid id,
        [FromBody] SendMessageRequest request)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new
            {
                code = "UNAUTHORIZED",
                message = "Không xác định được người dùng."
            });
        }

        try
        {
            var response = await _messageService.SendAsync(
                userId, id, request);

            if (response.IsNewlyCreated)
            {
                return Created(
                    $"/api/conversations/{id}/messages",
                    response);
            }

            return Ok(response);
        }
        catch (MessageServiceException exception)
        {
            return ServiceError(exception);
=======

using System.Security.Claims;
using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;

namespace Chat.Api.Controllers;

[ApiController]
[Route("api/conversations/{conversationId:guid}/messages")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;
    private readonly IAntiforgery _antiforgery;

    public MessagesController(
        IMessageService messageService,
        IAntiforgery antiforgery)
    {
        _messageService = messageService;
        _antiforgery = antiforgery;
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage(
        Guid conversationId,
        [FromBody] SendMessageRequest request)
    {
        if (!await IsValidCsrfTokenAsync())
        {
            return BadRequest(new
            {
                code = "INVALID_CSRF_TOKEN",
                message = "CSRF token is missing or invalid."
            });
        }

        var currentUserId = GetCurrentUserId();

        if (currentUserId == null)
            return Unauthorized(new
            {
                code = "UNAUTHORIZED",
                message = "Authentication is required."
            });

        try
        {
            var result = await _messageService.SendAsync(
                currentUserId.Value,
                conversationId,
                request);

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                code = "NOT_FOUND",
                message = "Conversation not found."
            });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, new
            {
                code = "FORBIDDEN",
                message = "You are not a member of this conversation."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                code = ex.Message,
                message = "Unable to send this message."
            });
>>>>>>> f50182b (Implement CHAT-16 group message visibility)
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory(
<<<<<<< HEAD
        [FromRoute] Guid id,
        [FromQuery] long? beforeSeq,
        [FromQuery] long? afterSeq,
        [FromQuery] int limit = 20)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new
            {
                code = "UNAUTHORIZED",
                message = "Không xác định được người dùng."
            });
        }

        if (beforeSeq.HasValue && afterSeq.HasValue)
        {
            return BadRequest(new
            {
                code = "INVALID_PAGINATION_PARAMETERS",
                message = "Không được truyền đồng thời beforeSeq và afterSeq."
            });
        }

        try
        {
            var messages = afterSeq.HasValue
                ? await _messageService.GetNewMessagesAsync(
                    userId, id, afterSeq.Value, limit)
                : await _messageService.GetHistoryAsync(
                    userId, id, beforeSeq, limit);

            return Ok(messages);
        }
        catch (MessageServiceException exception)
        {
            return ServiceError(exception);
        }
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        return Guid.TryParse(claim, out userId)
            && userId != Guid.Empty;
    }

    private ObjectResult ServiceError(MessageServiceException exception)
    {
        return StatusCode(exception.StatusCode, new
        {
            code = exception.Code,
            message = exception.Message
        });
    }
}
=======
        Guid conversationId,
        [FromQuery] long? beforeSeq = null,
        [FromQuery] int limit = 20)
    {
        var currentUserId = GetCurrentUserId();

        if (currentUserId == null)
            return Unauthorized(new
            {
                code = "UNAUTHORIZED",
                message = "Authentication is required."
            });

        try
        {
            var result = await _messageService.GetHistoryAsync(
                currentUserId.Value,
                conversationId,
                beforeSeq,
                limit);

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                code = "NOT_FOUND",
                message = "Conversation not found."
            });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, new
            {
                code = "FORBIDDEN",
                message = "You are not a member of this conversation."
            });
        }
    }

    [HttpGet("new")]
    public async Task<IActionResult> GetNewMessages(
        Guid conversationId,
        [FromQuery] long afterSeq,
        [FromQuery] int limit = 20)
    {
        var currentUserId = GetCurrentUserId();

        if (currentUserId == null)
            return Unauthorized(new
            {
                code = "UNAUTHORIZED",
                message = "Authentication is required."
            });

        try
        {
            var result = await _messageService.GetNewMessagesAsync(
                currentUserId.Value,
                conversationId,
                afterSeq,
                limit);

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                code = "NOT_FOUND",
                message = "Conversation not found."
            });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, new
            {
                code = "FORBIDDEN",
                message = "You are not a member of this conversation."
            });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        return Guid.TryParse(userId, out var id)
            ? id
            : null;
    }

    private async Task<bool> IsValidCsrfTokenAsync()
    {
        try
        {
            await _antiforgery.ValidateRequestAsync(HttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
>>>>>>> f50182b (Implement CHAT-16 group message visibility)
