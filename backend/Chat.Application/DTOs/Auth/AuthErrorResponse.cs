namespace Chat.Application.DTOs.Auth;

/// <summary>Body lỗi thống nhất theo API_CONTRACT: { "code": "UPPER_SNAKE_CASE", "message": "..." }.</summary>
public class AuthErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
