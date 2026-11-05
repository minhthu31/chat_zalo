namespace Chat.Application.DTOs.Friendships;

/// <summary>Public friendship/request representation. UserId always identifies the other user.</summary>
public sealed class FriendshipDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
