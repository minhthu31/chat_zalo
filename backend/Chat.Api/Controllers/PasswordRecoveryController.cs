using System.ComponentModel.DataAnnotations;
using Chat.Api.Filters;
using Chat.Api.Services;
using Chat.Application.DTOs.Auth;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Chat.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
[ValidateAntiforgeryJson]
[EnableRateLimiting("password-recovery")]
public sealed class PasswordRecoveryController(
    UserManager<ApplicationUser> users,
    PasswordRecovery recovery) : ControllerBase
{
    [HttpPost("forgot-password")]
    public IActionResult Forgot(ForgotPasswordRequest request)
    {
        if (!recovery.IsConfigured)
            return StatusCode(503, new AuthErrorResponse
            {
                Code = "PASSWORD_RECOVERY_UNAVAILABLE",
                Message = "Dịch vụ gửi mã đặt lại mật khẩu chưa được cấu hình."
            });

        var email = request.Email.Trim();
        if (!recovery.Enqueue(email, users.NormalizeEmail(email)!))
            return StatusCode(429);

        // The same result is returned for known and unknown email addresses.
        return Accepted();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> Reset(ResetPasswordRequest request)
    {
        var email = request.Email.Trim();
        var challenge = recovery.Consume(users.NormalizeEmail(email)!, request.Code);
        if (challenge is not { } valid) return InvalidCode();

        var user = await users.FindByIdAsync(valid.UserId.ToString());
        if (user is null || user.NormalizedEmail != users.NormalizeEmail(email))
            return InvalidCode();

        var result = await users.ResetPasswordAsync(user, valid.Token, request.NewPassword);
        if (result.Succeeded) return NoContent();

        if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            return InvalidCode();

        return BadRequest(new AuthErrorResponse
        {
            Code = "PASSWORD_RESET_FAILED",
            Message = "Không đặt được mật khẩu. Kiểm tra yêu cầu mật khẩu rồi gửi lại mã."
        });
    }

    private BadRequestObjectResult InvalidCode() =>
        BadRequest(new AuthErrorResponse
        {
            Code = "INVALID_RECOVERY_CODE",
            Message = "Mã không đúng hoặc đã hết hiệu lực."
        });

    public sealed class ForgotPasswordRequest
    {
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; init; } = "";
    }

    public sealed class ResetPasswordRequest
    {
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; init; } = "";

        [Required, RegularExpression("^[0-9]{6}$")]
        public string Code { get; init; } = "";

        [Required, StringLength(256)]
        public string NewPassword { get; init; } = "";
    }
}
