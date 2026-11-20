namespace Chat.Application.DTOs.Friendships;

/// <summary>Paged result of GET /api/friendships/search.</summary>
public sealed class PagedFriendSearchResponse
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    public List<SearchFriendDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}
