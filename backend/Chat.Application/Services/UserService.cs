
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

    public Task<UserDto?> GetCurrentUserAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.");

        return _users.FindByIdAsync(userId);
    }

    public Task<UserDto?> UpdateProfileAsync(
        string userId,
        UpdateProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.");

        if (request is null ||
            string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new ArgumentException("DisplayName is required.");
        }

        var displayName = request.DisplayName.Trim();

        if (displayName.Length > MaxDisplayNameLength)
        {
            throw new ArgumentException(
                $"DisplayName must not exceed {MaxDisplayNameLength} characters.");
        }

        return _users.UpdateDisplayNameAsync(userId, displayName);
    }

    public Task<IReadOnlyList<UserDto>> SearchUsersAsync(
        string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.");

        var normalizedUsername = username.Trim();

        if (normalizedUsername.Length > MaxUsernameLength)
        {
            throw new ArgumentException(
                $"Username must not exceed {MaxUsernameLength} characters.");
        }

        return _users.SearchByUsernameAsync(normalizedUsername);
    }
}
