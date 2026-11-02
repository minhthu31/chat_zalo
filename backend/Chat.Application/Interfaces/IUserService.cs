
using Chat.Application.DTOs.Users;

namespace Chat.Application.Interfaces;

public interface IUserService
{
    Task<UserDto?> GetCurrentUserAsync(string userId);

    Task<UserDto?> UpdateProfileAsync(
        string userId,
        UpdateProfileRequest request);

    Task<IReadOnlyList<UserDto>> SearchUsersAsync(
        string username);
}

/// <summary>
/// Abstraction for accessing user accounts through Identity.
/// </summary>
public interface IUserAccountGateway
{
    Task<UserDto?> FindByIdAsync(string userId);

    Task<UserDto?> UpdateDisplayNameAsync(
        string userId,
        string displayName);

    Task<IReadOnlyList<UserDto>> SearchByUsernameAsync(
        string username);
}
