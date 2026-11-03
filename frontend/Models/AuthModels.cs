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