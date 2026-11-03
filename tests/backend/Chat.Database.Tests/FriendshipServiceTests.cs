using Chat.Application.DTOs.Friendships;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Xunit;

namespace Chat.Database.Tests;

public sealed class FriendshipServiceTests
{
    [Fact]
    public async Task SendRequest_RejectsSelfFriendship()
    {
        var user = Guid.NewGuid();
        var service = new FriendshipService(new FakeFriendshipRepository(user));

        var error = await Assert.ThrowsAsync<FriendshipException>(
            () => service.SendRequestAsync(user, user));

        Assert.Equal("SELF_FRIEND_REQUEST", error.Code);
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task SendRequest_RejectsDuplicatePendingRequest()
    {
        var (sender, receiver) = NewPair();
        var repository = new FakeFriendshipRepository(sender, receiver);
        repository.Seed(sender, receiver, sender, "Pending");
        var service = new FriendshipService(repository);

        var error = await Assert.ThrowsAsync<FriendshipException>(
            () => service.SendRequestAsync(sender, receiver));

        Assert.Equal("FRIEND_REQUEST_ALREADY_EXISTS", error.Code);
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task Accept_OnlyRecipientCanAccept()
    {
        var (sender, receiver) = NewPair();
        var repository = new FakeFriendshipRepository(sender, receiver);
        repository.Seed(sender, receiver, sender, "Pending");
        var service = new FriendshipService(repository);

        var error = await Assert.ThrowsAsync<FriendshipException>(
            () => service.AcceptAsync(sender, receiver));

        Assert.Equal("ONLY_RECIPIENT_CAN_RESPOND", error.Code);
        Assert.Equal(403, error.StatusCode);
    }

    [Fact]
    public async Task Accept_MakesAreFriendsTrueInBothDirections()
    {
        var (sender, receiver) = NewPair();
        var repository = new FakeFriendshipRepository(sender, receiver);
        repository.Seed(sender, receiver, sender, "Pending");
        var service = new FriendshipService(repository);

        await service.AcceptAsync(receiver, sender);

        Assert.True(await service.AreFriendsAsync(sender, receiver));
        Assert.True(await service.AreFriendsAsync(receiver, sender));
    }

    [Fact]
    public async Task Reject_DoesNotMakeUsersFriends()
    {
        var (sender, receiver) = NewPair();
        var repository = new FakeFriendshipRepository(sender, receiver);
        repository.Seed(sender, receiver, sender, "Pending");
        var service = new FriendshipService(repository);

        await service.RejectAsync(receiver, sender);

        Assert.False(await service.AreFriendsAsync(sender, receiver));
        Assert.Equal("Rejected", repository.Relationship!.Status);
    }

    [Fact]
    public async Task RemovePendingRequest_CanOnlyBeDoneByRequester()
    {
        var (sender, receiver) = NewPair();
        var repository = new FakeFriendshipRepository(sender, receiver);
        repository.Seed(sender, receiver, sender, "Pending");
        var service = new FriendshipService(repository);

        var error = await Assert.ThrowsAsync<FriendshipException>(
            () => service.RemoveAsync(receiver, sender));

        Assert.Equal("ONLY_REQUESTER_CAN_CANCEL", error.Code);
        await service.RemoveAsync(sender, receiver);
        Assert.Equal("Cancelled", repository.Relationship!.Status);
    }

    [Fact]
    public async Task SendRequest_ReusesRejectedRelationshipAndResetsRequester()
    {
        var (first, second) = NewPair();
        var repository = new FakeFriendshipRepository(first, second);
        repository.Seed(first, second, first, "Rejected");
        var service = new FriendshipService(repository);

        var result = await service.SendRequestAsync(second, first);

        Assert.Equal(first, result.UserId);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(second, repository.Relationship!.RequesterUserId);
        Assert.Equal("Pending", repository.Relationship.Status);
    }

    [Fact]
    public async Task SendRequest_RejectsUnknownTarget()
    {
        var sender = Guid.NewGuid();
        var target = Guid.NewGuid();
        var repository = new FakeFriendshipRepository(sender);
        var service = new FriendshipService(repository);

        var error = await Assert.ThrowsAsync<FriendshipException>(
            () => service.SendRequestAsync(sender, target));

        Assert.Equal("USER_NOT_FOUND", error.Code);
        Assert.Equal(404, error.StatusCode);
    }


    [Fact]
    public async Task StaleRequestCannotChangeStatusAfterCancelAndResend()
    {
        var (sender, receiver) = NewPair();
        var repository = new FakeFriendshipRepository(sender, receiver);
        repository.Seed(sender, receiver, sender, "Pending");

        // Keep a snapshot representing the first request generation.
        var staleRequest = repository.Snapshot();
        Assert.True(await repository.TryChangeStatusAsync(staleRequest, "Pending", "Cancelled"));

        // A new request is created for the same pair and returns to Pending.
        repository.Seed(sender, receiver, receiver, "Pending");

        // The old request must not be able to accept/reject/cancel the new one.
        Assert.False(await repository.TryChangeStatusAsync(staleRequest, "Pending", "Accepted"));
        Assert.Equal("Pending", repository.Relationship!.Status);
        Assert.Equal(receiver, repository.Relationship.RequesterUserId);
    }

    [Fact]
    public async Task StaleRequestCannotReopenNewerRequestGeneration()
    {
        var (sender, receiver) = NewPair();
        var repository = new FakeFriendshipRepository(sender, receiver);
        repository.Seed(sender, receiver, sender, "Cancelled");
        var staleSnapshot = repository.Snapshot();

        repository.Seed(sender, receiver, receiver, "Cancelled");

        Assert.False(await repository.TryReopenAsync(staleSnapshot, sender));
        Assert.Equal("Cancelled", repository.Relationship!.Status);
        Assert.Equal(receiver, repository.Relationship.RequesterUserId);
    }

    private static (Guid Sender, Guid Receiver) NewPair()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        return first.CompareTo(second) < 0 ? (first, second) : (second, first);
    }

    private sealed class FakeFriendshipRepository : IFriendshipRepository
    {
        private readonly HashSet<Guid> _users;
        public Friendship? Relationship { get; private set; }

        public FakeFriendshipRepository(params Guid[] users)
        {
            _users = users.ToHashSet();
        }

        public void Seed(Guid low, Guid high, Guid requester, string status)
        {
            Relationship = new Friendship
            {
                UserLowId = low,
                UserHighId = high,
                RequesterUserId = requester,
                Status = status,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        public Friendship Snapshot() => new()
        {
            UserLowId = Relationship!.UserLowId,
            UserHighId = Relationship.UserHighId,
            RequesterUserId = Relationship.RequesterUserId,
            Status = Relationship.Status,
            CreatedAt = Relationship.CreatedAt,
            UpdatedAt = Relationship.UpdatedAt
        };

        public Task<bool> UserExistsAsync(Guid userId) => Task.FromResult(_users.Contains(userId));

        public Task<Friendship?> FindByPairAsync(Guid userLowId, Guid userHighId) =>
            Task.FromResult(Relationship is not null
                && Relationship.UserLowId == userLowId
                && Relationship.UserHighId == userHighId
                    ? Relationship
                    : null);

        public Task<FriendshipDto?> GetUserSummaryAsync(Guid userId, string status) =>
            Task.FromResult<FriendshipDto?>(_users.Contains(userId)
                ? new FriendshipDto { UserId = userId, Username = "test-user", DisplayName = "Test User", Status = status }
                : null);

        public Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId) =>
            Task.FromResult(new List<FriendshipDto>());

        public Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId) =>
            Task.FromResult(new FriendshipRequestsDto());

        public Task<bool> TryInsertAsync(Friendship friendship)
        {
            if (Relationship is not null)
                return Task.FromResult(false);
            Relationship = friendship;
            return Task.FromResult(true);
        }

        public Task<bool> TryReopenAsync(Friendship friendship, Guid requesterUserId)
        {
            if (Relationship is null
                || Relationship.Status != friendship.Status
                || Relationship.RequesterUserId != friendship.RequesterUserId
                || Relationship.CreatedAt != friendship.CreatedAt)
                return Task.FromResult(false);
            Relationship.RequesterUserId = requesterUserId;
            Relationship.Status = "Pending";
            Relationship.CreatedAt = DateTimeOffset.UtcNow;
            Relationship.UpdatedAt = null;
            return Task.FromResult(true);
        }

        public Task<bool> TryChangeStatusAsync(Friendship friendship, string expectedStatus, string newStatus)
        {
            if (Relationship is null
                || Relationship.Status != expectedStatus
                || Relationship.RequesterUserId != friendship.RequesterUserId
                || Relationship.CreatedAt != friendship.CreatedAt)
                return Task.FromResult(false);
            Relationship.Status = newStatus;
            Relationship.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(true);
        }
    }
}
