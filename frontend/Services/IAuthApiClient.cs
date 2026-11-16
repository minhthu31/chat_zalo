using Chat.Client.Models;

namespace Chat.Client.Services;

public interface IAuthApiClient
{
    Task<UserResponse> RegisterAsync(RegisterRequest request);
    Task<UserResponse> LoginAsync(LoginRequest request);
    Task LogoutAsync();
    Task<UserResponse?> GetMeAsync();   // null nghĩa là chưa đăng nhập (401)
}