using Chat.Application.Interfaces;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Services;

public class BlockService : IBlockService
{
    private readonly ChatDbContext _dbContext;

    public BlockService(ChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task BlockAsync(Guid currentUserId, Guid targetUserId)
        => throw new NotImplementedException();

    public Task UnblockAsync(Guid currentUserId, Guid targetUserId)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public async Task<bool> IsBlockedAsync(Guid userAId, Guid userBId)
    {
        // Bidirectional: either party may have blocked the other
        return await _dbContext.UserBlocks.AnyAsync(b =>
            (b.BlockerUserId == userAId && b.BlockedUserId == userBId) ||
            (b.BlockerUserId == userBId && b.BlockedUserId == userAId));
    }

    /// <inheritdoc/>
    public async Task<bool> HasBlockedAsync(Guid blockerUserId, Guid blockedUserId)
    {
        return await _dbContext.UserBlocks.AnyAsync(b =>
            b.BlockerUserId == blockerUserId && b.BlockedUserId == blockedUserId);
    }
}
