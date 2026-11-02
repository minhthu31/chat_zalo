using Chat.Application.DTOs.Auth;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Api.Filters;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Chat.Api.Controllers;

/// <summary>
/// API phụ trách: POST register, POST login, POST logout, GET me, GET csrf.
/// </summary>
[ApiController]
[Route("api/auth")]
[ValidateAntiforgeryJson]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAntiforgery _antiforgery;

    public AuthController(
        IAuthService auth,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAntiforgery antiforgery)
    {
        _auth = auth;
        _userManager = userManager;
        _signInManager = signInManager;
        _antiforgery = antiforgery;
    }

    /// <summary>
    /// Cấp CSRF token. FE gọi trước register/login và gọi lại sau khi login/logout
    /// (token gắn với danh tính hiện tại nên đổi sau khi đăng nhập).
    /// </summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    public ActionResult<CsrfTokenResponse> GetCsrfToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store";
        return Ok(new CsrfTokenResponse
        {
            Token = tokens.RequestToken ?? string.Empty
        });
    }

    // 201 + UserResponse, không tự đăng nhập. Trùng username/email: 409.
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest? request)
    {
        try
        {
            var user = await _auth.RegisterAsync(request!);
            return StatusCode(StatusCodes.Status201Created, user);
        }
        catch (AuthException ex)
        {
            return Error(ex);
        }
    }

    // 200 + UserResponse và phát hành cookie HttpOnly tại HTTP boundary. Sai thông tin: 401 INVALID_CREDENTIALS.
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request)
    {
        try
        {
            var user = await _auth.LoginAsync(request!);

            var appUser = await _userManager.FindByIdAsync(user.Id.ToString());
            if (appUser is null)
                return Error(new AuthException("INVALID_CREDENTIALS", "Tên đăng nhập/email hoặc mật khẩu không đúng.", 401));

            // Không tạo persistent cookie khi contract không có chức năng "Remember me".
            await _signInManager.SignInAsync(appUser, isPersistent: false);
            return Ok(user);
        }
        catch (AuthException ex)
        {
            return Error(ex);
        }
    }

    // 204 và xóa cookie. Chưa đăng nhập: 401 JSON.
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthenticated();

        await _auth.LogoutAsync();
        await _signInManager.SignOutAsync();
        return NoContent();
    }

    // 200 + UserResponse, hoặc 401 JSON khi chưa đăng nhập.
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthenticated();

        var appUser = await _userManager.GetUserAsync(User);
        if (appUser is null)
            return Unauthenticated();

        return Ok(new UserResponse
        {
            Id = appUser.Id,
            Username = appUser.UserName ?? string.Empty,
            DisplayName = appUser.DisplayName
        });
    }

    private IActionResult Error(AuthException ex) =>
        StatusCode(ex.StatusCode, new AuthErrorResponse { Code = ex.Code, Message = ex.Message });

    private IActionResult Unauthenticated() =>
        StatusCode(StatusCodes.Status401Unauthorized,
            new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Bạn chưa đăng nhập." });
}

/// <summary>
/// Cài đặt IAccountGateway bằng ASP.NET Core Identity. Đặt cùng file controller (thuộc phạm vi được giao)
/// vì Chat.Api là project duy nhất của phần auth tham chiếu được ApplicationUser mà không sửa Infrastructure.
/// </summary>
internal sealed class IdentityAccountGateway : IAccountGateway
{
    private readonly UserManager<ApplicationUser> _users;

    public IdentityAccountGateway(UserManager<ApplicationUser> users)
    {
        _users = users;
    }

    public async Task<AccountCreateResult> CreateAsync(string userName, string email, string password, string displayName)
    {
        // Kiểm tra trước để trả 409 ổn định theo contract, kể cả khi Identity
        // chưa bật RequireUniqueEmail. Cấu hình Identity vẫn nên bật tùy chọn này.
        if (await _users.FindByNameAsync(userName) is not null)
            return new AccountCreateResult(AccountCreateStatus.DuplicateUserName);

        if (await _users.FindByEmailAsync(email) is not null)
            return new AccountCreateResult(AccountCreateStatus.DuplicateEmail);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        IdentityResult result;
        try
        {
            result = await _users.CreateAsync(user, password);
        }
        catch (DbUpdateException)
        {
            // Hai request đăng ký trùng nhau chạy song song: unique index của DB là chốt chặn cuối.
            if (await _users.FindByNameAsync(userName) is not null)
                return new AccountCreateResult(AccountCreateStatus.DuplicateUserName);
            if (await _users.FindByEmailAsync(email) is not null)
                return new AccountCreateResult(AccountCreateStatus.DuplicateEmail);
            throw;
        }

        if (result.Succeeded)
            return new AccountCreateResult(AccountCreateStatus.Success, ToResponse(user));

        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
            return new AccountCreateResult(AccountCreateStatus.DuplicateUserName);

        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateEmail)))
            return new AccountCreateResult(AccountCreateStatus.DuplicateEmail);

        return new AccountCreateResult(
            AccountCreateStatus.InvalidInput,
            Error: string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    public async Task<CredentialResult> VerifyCredentialsAsync(string usernameOrEmail, string password)
    {
        var user = await _users.FindByNameAsync(usernameOrEmail)
                   ?? await _users.FindByEmailAsync(usernameOrEmail);

        if (user is null)
            return new CredentialResult(CredentialStatus.InvalidCredentials);

        if (await _users.IsLockedOutAsync(user))
            return new CredentialResult(CredentialStatus.LockedOut);

        if (!await _users.CheckPasswordAsync(user, password))
        {
            if (_users.SupportsUserLockout)
            {
                await _users.AccessFailedAsync(user);
                if (await _users.IsLockedOutAsync(user))
                    return new CredentialResult(CredentialStatus.LockedOut);
            }

            return new CredentialResult(CredentialStatus.InvalidCredentials);
        }

        if (_users.SupportsUserLockout)
            await _users.ResetAccessFailedCountAsync(user);

        return new CredentialResult(CredentialStatus.Success, ToResponse(user));
    }

    private static UserResponse ToResponse(ApplicationUser user) => new()
    {
        Id = user.Id,
        Username = user.UserName ?? string.Empty,
        DisplayName = user.DisplayName
    };
}
