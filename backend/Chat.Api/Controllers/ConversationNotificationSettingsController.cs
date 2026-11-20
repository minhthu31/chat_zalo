using System.Security.Claims;
using Chat.Api.Filters;
using Chat.Application.DTOs.Notifications;
using Chat.Application.Interfaces;
using Chat.Application.Models.Groups;
using Chat.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/conversations/{id}/notification-settings")]
public sealed class ConversationNotificationSettingsController : ControllerBase
{
    private readonly IConversationSettingsService _settingsService;
    private readonly ILogger<ConversationNotificationSettingsController>? _logger;

    public ConversationNotificationSettingsController(
        IConversationSettingsService settingsService,
        ILogger<ConversationNotificationSettingsController>? logger = null)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(new
            {
                code = "UNAUTHORIZED",
                message = "Bạn chưa đăng nhập."
            });
        }

        if (id == Guid.Empty)
        {
            return BadRequest(new
            {
                code = "VALIDATION_ERROR",
                message = "conversationId phải khác rỗng."
            });
        }

        try
        {
            var settings = await _settingsService.GetAsync(
                currentUserId,
                id,
                cancellationToken);

            return Ok(settings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HandleException(ex);
        }
    }

    [HttpPut]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateNotificationSettingsRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(new
            {
                code = "UNAUTHORIZED",
                message = "Bạn chưa đăng nhập."
            });
        }

        if (id == Guid.Empty || !ModelState.IsValid || request is null)
        {
            return BadRequest(new
            {
                code = "VALIDATION_ERROR",
                message = "Dữ liệu cài đặt thông báo không hợp lệ."
            });
        }

        try
        {
            var settings = await _settingsService.UpdateAsync(
                currentUserId,
                id,
                request,
                cancellationToken);

            return Ok(settings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HandleException(ex);
        }
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        userId = Guid.Empty;
        if (User?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        return Guid.TryParse(claim, out userId)
            && userId != Guid.Empty;
    }

    private IActionResult HandleException(Exception exception)
    {
        return exception switch
        {
            ConversationSettingsServiceException ex => StatusCode(ex.StatusCode, new
            {
                code = ex.Code,
                message = ex.Message
            }),
            GroupPermissionException ex => StatusCode(ex.HttpStatus, new
            {
                code = ex.ErrorCode,
                message = ex.Message
            }),
            KeyNotFoundException ex => NotFound(new
            {
                code = "NOT_FOUND",
                message = ex.Message
            }),
            ArgumentException ex => BadRequest(new
            {
                code = "VALIDATION_ERROR",
                message = ex.Message
            }),
            UnauthorizedAccessException ex => StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "CONVERSATION_ACCESS_DENIED",
                message = ex.Message
            }),
            _ => HandleUnhandledException(exception)
        };
    }

    private IActionResult HandleUnhandledException(Exception exception)
    {
        _logger?.LogError(exception, "Failed to process conversation notification settings request");
        return StatusCode(StatusCodes.Status500InternalServerError, new
        {
            code = "INTERNAL_ERROR",
            message = "Đã xảy ra lỗi hệ thống, vui lòng thử lại sau."
        });
    }
}

