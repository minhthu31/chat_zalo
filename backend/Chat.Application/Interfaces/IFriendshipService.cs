using Chat.Application.DTOs.Friendships;

namespace Chat.Application.Interfaces;

public interface IFriendshipService
{
    Task<FriendshipDto> SendRequestAsync(Guid currentUserId, Guid targetUserId);
    Task AcceptAsync(Guid currentUserId, Guid requesterUserId);
    Task RejectAsync(Guid currentUserId, Guid requesterUserId);
    Task RemoveAsync(Guid currentUserId, Guid targetUserId);
    Task<bool> AreFriendsAsync(Guid userAId, Guid userBId);
}
