using Chat.Api.Filters;
using Chat.Application.DTOs.Attachments;
using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Chat.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/conversations/{id}/messages")]
public sealed class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;
    private readonly IAttachmentMessageService _attachmentMessageService;

    public MessagesController(
        IMessageService messageService,
        IAttachmentMessageService attachmentMessageService)
    {
        _messageService = messageService;
        _attachmentMessageService = attachmentMessageService;
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

    [HttpPost("attachments")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> SendAttachmentMessage(
        [FromRoute] Guid id,
        [FromBody] SendAttachmentMessageRequest request,
        CancellationToken cancellationToken)
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
            var response = await _attachmentMessageService.SendAsync(
                userId,
                id,
                request,
                cancellationToken);

            if (response.Message.IsNewlyCreated)
            {
                return Created(
                    $"/api/conversations/{id}/messages",
                    response);
            }

            return Ok(response);
        }
        catch (AttachmentServiceException exception)
        {
            return AttachmentServiceError(exception);
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

    [HttpGet("attachment-details")]
    public async Task<IActionResult> GetAttachmentDetails(
        [FromRoute] Guid id,
        [FromQuery] string? messageIds,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new
            {
                code = "UNAUTHORIZED",
                message = "Unable to identify the current user."
            });
        }

        var parsedMessageIds = new List<Guid>();

        if (string.IsNullOrWhiteSpace(messageIds))
        {
            return BadRequest(new
            {
                code = "INVALID_MESSAGE_IDS",
                message = "messageIds must contain between 1 and 100 IDs."
            });
        }

        var rawMessageIds = messageIds.Split(',', StringSplitOptions.TrimEntries);

        if (rawMessageIds.Length is < 1 or > 100)
        {
            return BadRequest(new
            {
                code = "INVALID_MESSAGE_IDS",
                message = "messageIds must contain between 1 and 100 IDs."
            });
        }

        foreach (var rawId in rawMessageIds)
        {
            if (!Guid.TryParse(rawId, out var parsedId) || parsedId == Guid.Empty)
            {
                return BadRequest(new
                {
                    code = "INVALID_MESSAGE_IDS",
                    message = "messageIds contains an invalid ID."
                });
            }

            parsedMessageIds.Add(parsedId);
        }

        try
        {
            var details = await _attachmentMessageService.GetDetailsAsync(
                userId,
                id,
                parsedMessageIds,
                cancellationToken);

            return Ok(details);
        }
        catch (AttachmentServiceException exception)
        {
            return AttachmentServiceError(exception);
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

    private ObjectResult AttachmentServiceError(
        AttachmentServiceException exception)
    {
        return StatusCode(exception.StatusCode, new
        {
            code = exception.Code,
            message = exception.Message
        });
    }
}
