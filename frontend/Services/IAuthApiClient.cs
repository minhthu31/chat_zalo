using Chat.Client.Models;

namespace Chat.Client.Services;

public interface IAuthApiClient
{
    Task<UserResponse> RegisterAsync(RegisterRequest request);
    Task<UserResponse> LoginAsync(LoginRequest request);
    Task LogoutAsync();
    Task ChangePasswordAsync(ChangePasswordRequest request);   // 204: thành công, phiên cũ bị vô hiệu
    Task<UserResponse?> GetMeAsync();   // null nghĩa là chưa đăng nhập (401)
}