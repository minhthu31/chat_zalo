using System.Net.Mail;
using Chat.Application.DTOs.Auth;
using Chat.Application.Interfaces;

namespace Chat.Application.Services;

public sealed class AuthService : IAuthService
{
    // Giới hạn kích thước đầu vào để tránh payload bất thường.
    // do người phụ trách Program.cs/Identity quản lý.
    private const int MaxIdentifierLength = 256;
    private const int MaxUsernameLength = 100;
    private const int MaxEmailLength = 256;
    private const int MaxPasswordLength = 256;

    private readonly IAccountGateway _accounts;

    public AuthService(IAccountGateway accounts)
    {
        _accounts = accounts;
    }

    public async Task<UserResponse> RegisterAsync(RegisterRequest request)
    {
        var errors = ValidateRegister(request);
        if (errors.Count > 0)
            throw AuthException.Validation(string.Join(" ", errors));

        // DisplayName là cột bắt buộc trong DB; RegisterRequest không có trường này nên mặc định = username.
        var result = await _accounts.CreateAsync(request.Username!, request.Email!, request.Password!, request.Username!);

        return result.Status switch
        {
            AccountCreateStatus.Success when result.User is not null => result.User,
            AccountCreateStatus.DuplicateUserName =>
                throw new AuthException("USERNAME_ALREADY_EXISTS", "Tên đăng nhập đã được sử dụng.", 409),
            AccountCreateStatus.DuplicateEmail =>
                throw new AuthException("EMAIL_ALREADY_EXISTS", "Email đã được sử dụng.", 409),
            AccountCreateStatus.InvalidInput =>
                throw AuthException.Validation(result.Error ?? "Dữ liệu đăng ký không hợp lệ."),
            _ => throw new AuthException("INTERNAL_ERROR", "Không thể tạo tài khoản.", 500)
        };
    }

    public async Task<UserResponse> LoginAsync(LoginRequest request)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.UsernameOrEmail)
            || string.IsNullOrEmpty(request.Password)
            || request.UsernameOrEmail.Length > MaxIdentifierLength
            || request.Password.Length > MaxPasswordLength)
        {
            throw AuthException.Validation("Vui lòng nhập tên đăng nhập hoặc email và mật khẩu.");
        }

        var result = await _accounts.VerifyCredentialsAsync(request.UsernameOrEmail.Trim(), request.Password);

        return result.Status switch
        {
            CredentialStatus.Success when result.User is not null => result.User,
            CredentialStatus.LockedOut =>
                throw new AuthException("ACCOUNT_LOCKED", "Tài khoản tạm thời bị khóa do đăng nhập sai nhiều lần.", 401),
            _ => throw new AuthException("INVALID_CREDENTIALS", "Tên đăng nhập/email hoặc mật khẩu không đúng.", 401)
        };
    }

    // Cookie được xóa ở HTTP boundary (AuthController); service không phụ thuộc HttpContext.
    public Task LogoutAsync() => Task.CompletedTask;

    private static List<string> ValidateRegister(RegisterRequest? request)
    {
        var errors = new List<string>();
        if (request is null)
        {
            errors.Add("Dữ liệu đăng ký không được để trống.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.Username)
            || request.Username.Length > MaxUsernameLength)
            errors.Add("Tên đăng nhập không được để trống và không vượt quá 100 ký tự.");

        if (!IsValidEmail(request.Email))
            errors.Add("Email không hợp lệ.");

        // Chính sách độ mạnh mật khẩu do cấu hình Identity (Program.cs) quyết định.
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > MaxPasswordLength)
            errors.Add("Mật khẩu không được để trống và không vượt quá 256 ký tự.");

        return errors;
    }

    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrEmpty(email) || email.Length > MaxEmailLength) return false;
        if (!MailAddress.TryCreate(email, out var parsed)) return false;
        return string.Equals(parsed.Address, email, StringComparison.Ordinal)
               && parsed.Host.Contains(".", StringComparison.Ordinal);
    }
}

/// <summary>Lỗi nghiệp vụ có mã thống nhất; AuthController chuyển thành JSON { code, message }.</summary>
public sealed class AuthException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }

    public AuthException(string code, string message, int statusCode) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public static AuthException Validation(string message) => new("VALIDATION_ERROR", message, 400);
}

public enum AccountCreateStatus
{
    Success,
    DuplicateUserName,
    DuplicateEmail,
    InvalidInput
}

public sealed record AccountCreateResult(AccountCreateStatus Status, UserResponse? User = null, string? Error = null);

public enum CredentialStatus
{
    Success,
    InvalidCredentials,
    LockedOut
}

public sealed record CredentialResult(CredentialStatus Status, UserResponse? User = null);

/// <summary>
/// Cổng tới kho tài khoản. Chat.Application không tham chiếu được ApplicationUser (nằm ở Infrastructure)
/// nên AuthService làm việc qua interface này; bản cài đặt dùng ASP.NET Core Identity nằm trong AuthController.cs.
/// </summary>
public interface IAccountGateway
{
    Task<AccountCreateResult> CreateAsync(string userName, string email, string password, string displayName);

    Task<CredentialResult> VerifyCredentialsAsync(string usernameOrEmail, string password);
}
