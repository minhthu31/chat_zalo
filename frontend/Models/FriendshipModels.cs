using System.Text.Json.Serialization;

namespace Chat.Client.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FriendshipStatus
{
    Pending,
    Accepted,
    Rejected,
    Cancelled
}

public class FriendRequestDto
{
    public Guid TargetUserId { get; set; }
}

public class FriendshipDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public FriendshipStatus Status { get; set; }
}

public class FriendRequestsResponse
{
    public List<FriendshipDto> Incoming { get; set; } = new();
    public List<FriendshipDto> Outgoing { get; set; } = new();
}
// Kết quả của GET /api/friendships/search (hợp đồng Sprint 3, CHAT-30): chỉ tìm trong bạn bè đã Accepted
public class SearchFriendDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class PagedFriendSearchResponse
{
    public List<SearchFriendDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}
