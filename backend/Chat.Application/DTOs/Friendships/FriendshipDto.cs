namespace Chat.Application.DTOs.Friendships;

/// <summary>
/// Public friendship representation. UserId always identifies the other user
/// relative to the authenticated caller.
/// </summary>
public sealed class FriendshipDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>Pending invitations received and sent by the current user.</summary>
public sealed class FriendshipRequestsDto
{
    public List<FriendshipDto> Incoming { get; set; } = [];
    public List<FriendshipDto> Outgoing { get; set; } = [];
}
