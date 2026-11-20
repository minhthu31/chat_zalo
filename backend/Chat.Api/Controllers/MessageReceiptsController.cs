using System.Security.Claims;
using Chat.Api.Filters;
using Chat.Application.DTOs.Receipts;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(Error("UNAUTHORIZED", "Bạn chưa đăng nhập."));

        try
        {
            var result = await _receipts.GetAsync(userId, id, cancellationToken);
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
        [FromBody] UpdateReceiptRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(Error("UNAUTHORIZED", "Bạn chưa đăng nhập."));

        try
        {
            var result = await _receipts.MarkDeliveredAsync(
                userId, id, request, cancellationToken);
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
        [FromBody] UpdateReceiptRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(Error("UNAUTHORIZED", "Bạn chưa đăng nhập."));

        try
        {
            var result = await _receipts.MarkReadAsync(
                userId, id, request, cancellationToken);
            return Ok(result);
        }
        catch (ReceiptServiceException exception)
        {
            return ServiceError(exception);
        }
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
