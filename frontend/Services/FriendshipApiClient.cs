using Chat.Client.Models;

namespace Chat.Client.Services;

// Bản thật của IFriendshipApiClient
public class FriendshipApiClient : IFriendshipApiClient
{
    private readonly ApiRequestSender _api;

    public FriendshipApiClient(ApiRequestSender api)
    {
        _api = api;
    }

    public Task<FriendshipDto> SendRequestAsync(Guid targetUserId) =>
        _api.SendAsync<FriendshipDto>(HttpMethod.Post, "api/friendships",
            new FriendRequestDto { TargetUserId = targetUserId });

    public Task<List<FriendshipDto>> GetFriendsAsync() =>
        _api.SendAsync<List<FriendshipDto>>(HttpMethod.Get, "api/friendships");

    public Task<FriendRequestsResponse> GetRequestsAsync() =>
        _api.SendAsync<FriendRequestsResponse>(HttpMethod.Get, "api/friendships/requests");

    public Task AcceptAsync(Guid userId) =>
        _api.SendAsync(HttpMethod.Post, $"api/friendships/{userId}/accept");

    public Task RejectAsync(Guid userId) =>
        _api.SendAsync(HttpMethod.Post, $"api/friendships/{userId}/reject");

    public Task RemoveAsync(Guid userId) =>
        _api.SendAsync(HttpMethod.Delete, $"api/friendships/{userId}");
}
