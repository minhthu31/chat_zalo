using Chat.Application.DTOs.Friendships;
using Chat.Domain.Entities;

namespace Chat.Application.Interfaces;

/// <summary>
/// Persistence boundary for friendships. The Application layer does not depend
/// on EF Core or Infrastructure.
/// </summary>
public interface IFriendshipRepository
{
    Task<bool> UserExistsAsync(Guid userId);
    Task<Friendship?> FindByPairAsync(Guid userLowId, Guid userHighId);
    Task<FriendshipDto?> GetUserSummaryAsync(Guid userId, string status);
    Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId);
    Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId);
    /// <returns>false when the unique pair key was inserted concurrently.</returns>
    Task<bool> TryInsertAsync(Friendship friendship);
    Task<bool> TryReopenAsync(Friendship friendship, Guid requesterUserId);
    Task<bool> TryChangeStatusAsync(Friendship friendship, string expectedStatus, string newStatus);
}
