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