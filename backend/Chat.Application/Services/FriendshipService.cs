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

    /// <inheritdoc/>
    public async Task<bool> AreFriendsAsync(Guid userAId, Guid userBId, CancellationToken ct = default)
    {
        // Normalise the pair: userLowId < userHighId (ordinal "D"-format string comparison)
        var (low, high) = string.Compare(
            userAId.ToString("D"), userBId.ToString("D"),
            StringComparison.OrdinalIgnoreCase) < 0
            ? (userAId, userBId)
            : (userBId, userAId);

        return await _dbContext.Friendships.AnyAsync(
            f => f.UserLowId == low && f.UserHighId == high && f.Status == "Accepted", ct);
    }
}

