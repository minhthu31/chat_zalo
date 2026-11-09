namespace Chat.Application.DTOs.Users;

/// <summary>
/// Paged result of GET /api/users/search/paged.
/// Only exposes public user fields through <see cref="UserDto"/>.
/// </summary>
public sealed class PagedUsersResponse
{
    /// <summary>Page size used when the client does not send pageSize.</summary>
    public const int DefaultPageSize = 10;

    /// <summary>Largest page size a client may request.</summary>
    public const int MaxPageSize = 100;

    public List<UserDto> Items { get; set; } = new();

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }
}
