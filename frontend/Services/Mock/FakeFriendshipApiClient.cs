using Chat.Client.Models;

namespace Chat.Client.Services.Mock;

public class FakeFriendshipApiClient : IFriendshipApiClient
{
    private readonly List<FriendshipDto> _friends;
    private readonly List<FriendshipDto> _incoming;
    private readonly List<FriendshipDto> _outgoing = new();

    public FakeFriendshipApiClient()
    {
        _friends = new() { ToDto(FakeUserApiClient.Directory[0], FriendshipStatus.Accepted) };
        _incoming = new() { ToDto(FakeUserApiClient.Directory[1], FriendshipStatus.Pending) };
    }

    public async Task<FriendshipDto> SendRequestAsync(Guid targetUserId)
    {
        await Task.Delay(300);

        var user = FakeUserApiClient.Directory.FirstOrDefault(u => u.Id == targetUserId)
                   ?? throw new ApiException(404, "USER_NOT_FOUND", "User not found");

        if (_friends.Any(f => f.UserId == targetUserId))
            throw new ApiException(409, "ALREADY_FRIENDS", "Already friends");

        if (_outgoing.Any(f => f.UserId == targetUserId) || _incoming.Any(f => f.UserId == targetUserId))
            throw new ApiException(409, "FRIEND_REQUEST_ALREADY_EXISTS", "Friend request already exists");

        var dto = ToDto(user, FriendshipStatus.Pending);
        _outgoing.Add(dto);
        return dto;
    }

    public async Task<List<FriendshipDto>> GetFriendsAsync()
    {
        await Task.Delay(300);
        return new List<FriendshipDto>(_friends);
    }

    public async Task<FriendRequestsResponse> GetRequestsAsync()
    {
        await Task.Delay(300);
        return new FriendRequestsResponse
        {
            Incoming = new List<FriendshipDto>(_incoming),
            Outgoing = new List<FriendshipDto>(_outgoing)
        };
    }

    public async Task AcceptAsync(Guid userId)
    {
        await Task.Delay(300);

        var request = _incoming.FirstOrDefault(f => f.UserId == userId)
                      ?? throw new ApiException(404, "FRIENDSHIP_NOT_FOUND", "Friend request not found");

        _incoming.Remove(request);
        _friends.Add(new FriendshipDto
        {
            UserId = request.UserId,
            Username = request.Username,
            DisplayName = request.DisplayName,
            Status = FriendshipStatus.Accepted
        });
    }

    public async Task RejectAsync(Guid userId)
    {
        await Task.Delay(300);

        var request = _incoming.FirstOrDefault(f => f.UserId == userId)
                      ?? throw new ApiException(404, "FRIENDSHIP_NOT_FOUND", "Friend request not found");

        _incoming.Remove(request);
    }

    // Hủy lời mời đã gửi hoặc hủy kết bạn
    public async Task RemoveAsync(Guid userId)
    {
        await Task.Delay(300);

        var removed = _outgoing.RemoveAll(f => f.UserId == userId) + _friends.RemoveAll(f => f.UserId == userId);
        if (removed == 0)
            throw new ApiException(404, "FRIENDSHIP_NOT_FOUND", "Friendship not found");
    }

    private static FriendshipDto ToDto(UserDto user, FriendshipStatus status) => new()
    {
        UserId = user.Id,
        Username = user.Username,
        DisplayName = user.DisplayName,
        Status = status
    };
}
