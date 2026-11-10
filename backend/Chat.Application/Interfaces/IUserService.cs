
using Chat.Application.DTOs.Users;

namespace Chat.Application.Interfaces;

public interface IUserService
{
    Task<UserDto> GetProfileAsync(Guid userId);

    Task<List<UserDto>> SearchAsync(string username);

    Task<PagedUsersResponse> SearchPagedAsync(
        string username,
        int page,
        int pageSize);

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

    /// <summary>
    /// Searches users by username with counting and paging executed in the
    /// data store. Returns the requested page and the total number of matches.
    /// </summary>
    /// <remarks>
    /// Default implementation keeps existing gateway implementations compiling;
    /// the Identity gateway provides the database-backed implementation.
    /// </remarks>
    Task<(IReadOnlyList<UserDto> Items, int TotalCount)> SearchByUsernamePagedAsync(
        string username,
        int page,
        int pageSize)
        => throw new NotSupportedException(
            "Paged username search is not supported by this gateway.");
}
