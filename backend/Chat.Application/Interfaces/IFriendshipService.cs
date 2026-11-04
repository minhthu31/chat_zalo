using Chat.Application.DTOs.Friendships;


namespace Chat.Application.Interfaces;

/// <summary>Friendship use cases. The first five signatures are fixed by API_CONTRACT.md v1.2.</summary>
public interface IFriendshipService
{
    Task<FriendshipDto> SendRequestAsync(Guid currentUserId, Guid targetUserId);
    Task AcceptAsync(Guid currentUserId, Guid requesterUserId);
    Task RejectAsync(Guid currentUserId, Guid requesterUserId);
    Task RemoveAsync(Guid currentUserId, Guid targetUserId);
    Task<bool> AreFriendsAsync(Guid userAId, Guid userBId);

    // Read endpoints required by section 4.3 of API_CONTRACT.md v1.2.
    Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId);
    Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId);
    
}
