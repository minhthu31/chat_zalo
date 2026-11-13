using Chat.Client.Models;

namespace Chat.Client.Services;

// Bản thật của IUserApiClient
public class UserApiClient : IUserApiClient
{
    private readonly ApiRequestSender _api;

    public UserApiClient(ApiRequestSender api)
    {
        _api = api;
    }

    public Task<UserDto> GetProfileAsync() =>
        _api.SendAsync<UserDto>(HttpMethod.Get, "api/users/me");

    public Task<UserDto> UpdateProfileAsync(UpdateProfileRequest request) =>
        _api.SendAsync<UserDto>(HttpMethod.Patch, "api/users/me", request);

    // Uri.EscapeDataString để ký tự đặc biệt trong tên không làm hỏng URL
    public Task<List<UserDto>> SearchAsync(string username) =>
        _api.SendAsync<List<UserDto>>(HttpMethod.Get, $"api/users/search?username={Uri.EscapeDataString(username)}");

    // Bản có phân trang: page bắt đầu từ 1, pageSize mặc định 10, tối đa 100
    public Task<PagedUsersResponse> SearchPagedAsync(string username, int page, int pageSize) =>
        _api.SendAsync<PagedUsersResponse>(HttpMethod.Get,
            $"api/users/search/paged?username={Uri.EscapeDataString(username)}&page={page}&pageSize={pageSize}");
}
