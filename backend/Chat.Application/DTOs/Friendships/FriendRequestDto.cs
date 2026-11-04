namespace Chat.Application.DTOs.Friendships;

/// <summary>Payload used to send a friend request.</summary>
public sealed class FriendRequestDto
{
    public Guid TargetUserId { get; set; }
}
