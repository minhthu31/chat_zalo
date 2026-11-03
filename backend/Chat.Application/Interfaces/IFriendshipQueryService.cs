using Chat.Application.DTOs.Friendships;

namespace Chat.Application.Interfaces;

/// <summary>
/// Read-only friendship queries kept separate from the v1.2
/// IFriendshipService contract.
/// </summary>
public interface IFriendshipQueryService
{
    Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId);
    Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId);
}
