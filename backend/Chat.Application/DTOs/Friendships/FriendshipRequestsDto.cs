namespace Chat.Application.DTOs.Friendships;

public class FriendshipRequestsDto
{
    public List<FriendshipDto> Incoming { get; set; } = new();
    public List<FriendshipDto> Outgoing { get; set; } = new();
}