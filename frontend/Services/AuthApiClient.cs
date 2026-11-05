using Chat.Client.Models;

namespace Chat.Client.Services;

// Bản thật của IAuthApiClient: gọi các endpoint /api/auth/* của backend
public class AuthApiClient : IAuthApiClient
{
    private readonly ApiRequestSender _api;

    public AuthApiClient(ApiRequestSender api)
    {
        _api = api;
    }

    // 201: đăng ký xong KHÔNG tự đăng nhập
    public Task<UserResponse> RegisterAsync(RegisterRequest request) =>
        _api.SendAsync<UserResponse>(HttpMethod.Post, "api/auth/register", request);

    public async Task<UserResponse> LoginAsync(LoginRequest request)
    {
        var user = await _api.SendAsync<UserResponse>(HttpMethod.Post, "api/auth/login", request);
        _api.ResetCsrfToken(); // đăng nhập xong danh tính đổi nên token cũ không còn đúng
        return user;
    }

    public async Task LogoutAsync()
    {
        await _api.SendAsync(HttpMethod.Post, "api/auth/logout");
        _api.ResetCsrfToken();
    }

    public async Task<UserResponse?> GetMeAsync()
    {
        try
        {
            return await _api.SendAsync<UserResponse>(HttpMethod.Get, "api/auth/me");
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            return null; // chưa đăng nhập
        }
    }
}
