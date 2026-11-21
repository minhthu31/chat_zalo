using System.Security.Claims;
using System.Text.Json;
using Chat.Api.Filters;
using Chat.Application.DTOs.Receipts;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Chat.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/conversations/{id:guid}/receipts")]
public sealed class MessageReceiptsController : ControllerBase
{
    private readonly IMessageReceiptService _receipts;

    public MessageReceiptsController(IMessageReceiptService receipts)
    {
        _receipts = receipts;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromRoute] Guid id,
        [FromQuery(Name = "messageIds")] string? rawMessageIds,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(Error("UNAUTHORIZED", "Bạn chưa đăng nhập."));

        HashSet<Guid>? messageIds = null;
        if (rawMessageIds is not null)
        {
            var parts = rawMessageIds.Split(',', IMessageReceiptService.MaxMessageIds + 1,
                StringSplitOptions.TrimEntries);
            if (parts.Length > IMessageReceiptService.MaxMessageIds)
                return InvalidMessageIds();

            messageIds = [];
            foreach (var part in parts)
            {
                if (!Guid.TryParse(part, out var messageId) || messageId == Guid.Empty)
                    return InvalidMessageIds();
                messageIds.Add(messageId);
            }
        }

        try
        {
            var result = await _receipts.GetAsync(userId, id, cancellationToken, messageIds);
            return Ok(result);
        }
        catch (ReceiptServiceException exception)
        {
            return ServiceError(exception);
        }
    }

    [HttpPut("delivered")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> MarkDelivered(
        [FromRoute] Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(Error("UNAUTHORIZED", "Bạn chưa đăng nhập."));

        if (!TryParseReceipt(request, out var parsed))
            return BadRequest(Error("INVALID_RECEIPT_SEQ", "seq phải là số nguyên không âm."));

        try
        {
            var result = await _receipts.MarkDeliveredAsync(
                userId, id, parsed, cancellationToken);
            return Ok(result);
        }
        catch (ReceiptServiceException exception)
        {
            return ServiceError(exception);
        }
    }

    [HttpPut("read")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> MarkRead(
        [FromRoute] Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(Error("UNAUTHORIZED", "Bạn chưa đăng nhập."));

        if (!TryParseReceipt(request, out var parsed))
            return BadRequest(Error("INVALID_RECEIPT_SEQ", "seq phải là số nguyên không âm."));

        try
        {
            var result = await _receipts.MarkReadAsync(
                userId, id, parsed, cancellationToken);
            return Ok(result);
        }
        catch (ReceiptServiceException exception)
        {
            return ServiceError(exception);
        }
    }

    private BadRequestObjectResult InvalidMessageIds() =>
        BadRequest(Error("VALIDATION_ERROR", "messageIds phải chứa từ 1 đến 100 ID tin nhắn hợp lệ."));

    private static bool TryParseReceipt(JsonElement json, out UpdateReceiptRequest request)
    {
        request = new UpdateReceiptRequest();
        if (json.ValueKind != JsonValueKind.Object ||
            !json.TryGetProperty("seq", out var seq) ||
            seq.ValueKind != JsonValueKind.Number ||
            !seq.TryGetInt64(out var value) || value < 0)
            return false;
        request.Seq = value;
        return true;
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        return Guid.TryParse(claim, out userId) && userId != Guid.Empty;
    }

    private ObjectResult ServiceError(ReceiptServiceException exception) =>
        StatusCode(exception.StatusCode, Error(exception.Code, exception.Message));

    private static object Error(string code, string message) => new { code, message };
}
