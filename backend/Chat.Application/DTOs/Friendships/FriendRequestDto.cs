namespace Chat.Application.DTOs.Friendships;

/// <summary>Request body for POST /api/friendships.</summary>
public sealed class FriendRequestDto
{
    public Guid TargetUserId { get; set; }
}
