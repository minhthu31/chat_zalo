
using Chat.Api.Filters;
using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

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
                message = "Unable to identify the current user."
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
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory(
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
                message = "Unable to identify the current user."
            });
        }

        if (beforeSeq.HasValue && afterSeq.HasValue)
        {
            return BadRequest(new
            {
                code = "INVALID_PAGINATION_PARAMETERS",
                message = "beforeSeq and afterSeq cannot be used together."
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
