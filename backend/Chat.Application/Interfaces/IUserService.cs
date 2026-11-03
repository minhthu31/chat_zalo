
using Chat.Application.DTOs.Users;

namespace Chat.Application.Interfaces;

public interface IUserService
{
    Task<UserDto> GetProfileAsync(Guid userId);

    Task<List<UserDto>> SearchAsync(string username);

    Task<UserDto> UpdateProfileAsync(
        Guid userId,
        UpdateProfileRequest request);
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
