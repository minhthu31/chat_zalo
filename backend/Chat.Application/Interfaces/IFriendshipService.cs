using Chat.Application.DTOs.Friendships;


namespace Chat.Application.Interfaces;

/// <summary>Friendship use cases. The first five signatures are fixed by API_CONTRACT.md v1.2.</summary>
public interface IFriendshipService
{
    Task<FriendshipDto> SendRequestAsync(Guid currentUserId, Guid targetUserId);
    Task AcceptAsync(Guid currentUserId, Guid requesterUserId);
    Task RejectAsync(Guid currentUserId, Guid requesterUserId);
    Task RemoveAsync(Guid currentUserId, Guid targetUserId);
    /// <summary>
    /// Checks the friendship between one inviter and one invitee.
    /// Returns true only for an Accepted relationship; group invitation
    /// authorization must evaluate each inviter/invitee pair independently.
    /// </summary>
    Task<bool> AreFriendsAsync(Guid userAId, Guid userBId);

    // Read endpoints required by section 4.3 of API_CONTRACT.md v1.2.
    /// <summary>Returns only Accepted friends belonging to the specified user.</summary>
    Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId);
    Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId);
    
}
