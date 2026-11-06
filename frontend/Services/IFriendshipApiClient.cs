using Chat.Client.Models;

namespace Chat.Client.Services;

public interface IFriendshipApiClient
{
    Task<FriendshipDto> SendRequestAsync(Guid targetUserId);   // POST /api/friendships
    Task<List<FriendshipDto>> GetFriendsAsync();               // GET /api/friendships (chỉ Accepted)
    Task<FriendRequestsResponse> GetRequestsAsync();           // GET /api/friendships/requests
    Task AcceptAsync(Guid userId);                             // POST /api/friendships/{userId}/accept
    Task RejectAsync(Guid userId);                             // POST /api/friendships/{userId}/reject
    Task RemoveAsync(Guid userId);                             // DELETE /api/friendships/{userId}
}
