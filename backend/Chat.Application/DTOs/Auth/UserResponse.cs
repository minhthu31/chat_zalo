namespace Chat.Application.DTOs.Auth;

/// <summary>Dùng cho login và auth/me. Không chứa email, mật khẩu hay hash.</summary>
public class UserResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
