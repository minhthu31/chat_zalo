namespace Chat.Application.DTOs.Auth;

// Kiểu nullable để AuthService tự báo lỗi VALIDATION_ERROR khi thiếu trường.
public class RegisterRequest
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
}
