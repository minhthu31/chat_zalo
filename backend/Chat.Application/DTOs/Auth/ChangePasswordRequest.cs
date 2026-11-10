namespace Chat.Application.DTOs.Auth;

// Kiểu nullable để AuthService tự báo lỗi VALIDATION_ERROR khi thiếu trường.
public class ChangePasswordRequest
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
}
