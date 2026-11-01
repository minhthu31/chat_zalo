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

    /// <inheritdoc/>
    public async Task<bool> IsBlockedAsync(Guid userAId, Guid userBId, CancellationToken ct = default)
    {
        // Bidirectional: either party may have blocked the other
        return await _dbContext.UserBlocks.AnyAsync(b =>
            (b.BlockerUserId == userAId && b.BlockedUserId == userBId) ||
            (b.BlockerUserId == userBId && b.BlockedUserId == userAId), ct);
    }

    /// <inheritdoc/>
    public async Task<bool> HasBlockedAsync(Guid blockerUserId, Guid blockedUserId, CancellationToken ct = default)
    {
        return await _dbContext.UserBlocks.AnyAsync(b =>
            b.BlockerUserId == blockerUserId && b.BlockedUserId == blockedUserId, ct);
    }
}

