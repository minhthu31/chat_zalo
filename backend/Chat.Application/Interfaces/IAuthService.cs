using Chat.Application.DTOs.Auth;

namespace Chat.Application.Interfaces;

// Chữ ký đúng API_CONTRACT mục 5.1 - không tự ý đổi.
public interface IAuthService
{
    Task<UserResponse> RegisterAsync(RegisterRequest request);

    Task<UserResponse> LoginAsync(LoginRequest request);

    Task LogoutAsync();
}
