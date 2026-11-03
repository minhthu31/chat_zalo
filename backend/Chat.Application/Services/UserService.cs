
using Chat.Application.DTOs.Users;
using Chat.Application.Interfaces;

namespace Chat.Application.Services;

public sealed class UserService : IUserService
{
    private const int MaxUsernameLength = 100;
    private const int MaxDisplayNameLength = 100;

    private readonly IUserAccountGateway _users;

    public UserService(IUserAccountGateway users)
    {
        _users = users;
    }

    public async Task<UserDto> GetProfileAsync(Guid userId)
    {
        var user = await _users.FindByIdAsync(userId.ToString());

        return user ?? throw new KeyNotFoundException("User not found.");
    }

    public async Task<List<UserDto>> SearchAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required.");
        }

        var normalizedUsername = username.Trim();

        if (normalizedUsername.Length > MaxUsernameLength)
        {
            throw new ArgumentException(
                $"Username must not exceed {MaxUsernameLength} characters.");
        }

        var users = await _users.SearchByUsernameAsync(normalizedUsername);

        return users.ToList();
    }

    public async Task<UserDto> UpdateProfileAsync(
        Guid userId,
        UpdateProfileRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new ArgumentException("DisplayName is required.");
        }

        var displayName = request.DisplayName.Trim();

        if (displayName.Length > MaxDisplayNameLength)
        {
            throw new ArgumentException(
                $"DisplayName must not exceed {MaxDisplayNameLength} characters.");
        }

        var user = await _users.UpdateDisplayNameAsync(
            userId.ToString(),
            displayName);

        return user ?? throw new KeyNotFoundException("User not found.");
    }
}
