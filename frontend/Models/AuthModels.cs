namespace Chat.Client.Models;

public class RegisterRequest
{
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public class LoginRequest
{
    public string UsernameOrEmail { get; set; } = "";
    public string Password { get; set; } = "";
}

// Body của POST /api/auth/change-password (hợp đồng Sprint 2, mục 9)
public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class UserResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class CsrfResponse
{
    public string Token { get; set; } = "";
}

public class ApiError
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}