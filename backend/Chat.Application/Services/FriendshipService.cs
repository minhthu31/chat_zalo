using Chat.Application.DTOs.Friendships;
using Chat.Application.Interfaces;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Services;

public class FriendshipService : IFriendshipService
{
    private readonly ChatDbContext _dbContext;

    public FriendshipService(ChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<FriendshipDto> SendRequestAsync(Guid currentUserId, Guid targetUserId)
        => throw new NotImplementedException();

    public Task AcceptAsync(Guid currentUserId, Guid requesterUserId)
        => throw new NotImplementedException();

    public Task RejectAsync(Guid currentUserId, Guid requesterUserId)
        => throw new NotImplementedException();

    public Task RemoveAsync(Guid currentUserId, Guid targetUserId)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public async Task<bool> AreFriendsAsync(Guid userAId, Guid userBId)
    {
        // Normalise the pair: userLowId < userHighId (ordinal "D"-format string comparison)
        var (low, high) = string.Compare(
            userAId.ToString("D"), userBId.ToString("D"),
            StringComparison.OrdinalIgnoreCase) < 0
            ? (userAId, userBId)
            : (userBId, userAId);

        return await _dbContext.Friendships.AnyAsync(
            f => f.UserLowId == low && f.UserHighId == high && f.Status == "Accepted");
    }
}
