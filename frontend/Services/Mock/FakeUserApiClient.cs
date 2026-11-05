using Chat.Client.Models;

namespace Chat.Client.Services.Mock;

public class FakeUserApiClient : IUserApiClient
{
    // Danh bạ giả dùng chung cho cả FakeFriendshipApiClient
    public static readonly List<UserDto> Directory = new()
    {
        new() { Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440002"), Username = "user02", DisplayName = "Nguyen Van B" },
        new() { Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440007"), Username = "user03", DisplayName = "Tran Thi C" },
        new() { Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440008"), Username = "user04", DisplayName = "Le Van D" }
    };

    private UserDto _me = new()
    {
        Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440001"),
        Username = "user01",
        DisplayName = "Nguyen Van A"
    };

    public async Task<UserDto> GetProfileAsync()
    {
        await Task.Delay(300);
        return Copy(_me);
    }

    public async Task<UserDto> UpdateProfileAsync(UpdateProfileRequest request)
    {
        await Task.Delay(300);

        var name = request.DisplayName.Trim();
        if (name == "" || name.Length > 100)
            throw new ApiException(400, "INVALID_DISPLAY_NAME", "DisplayName is invalid");

        _me = new UserDto { Id = _me.Id, Username = _me.Username, DisplayName = name };
        return Copy(_me);
    }

    public async Task<List<UserDto>> SearchAsync(string username)
    {
        await Task.Delay(300);

        if (string.IsNullOrWhiteSpace(username))
            throw new ApiException(400, "USERNAME_REQUIRED", "Username is required");

        return Directory
            .Where(u => u.Username.Contains(username.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(Copy)
            .ToList();
    }

    private static UserDto Copy(UserDto u) => new() { Id = u.Id, Username = u.Username, DisplayName = u.DisplayName };
}
