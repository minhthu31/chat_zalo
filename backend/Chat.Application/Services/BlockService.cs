using Chat.Application.Interfaces;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Services;

public sealed class BlockService(ChatDbContext db) : IBlockService
{
    public async Task BlockAsync(Guid currentUserId, Guid targetUserId)
    {
        if (currentUserId == targetUserId)
            throw new BlockException(400, "SELF_BLOCK_NOT_ALLOWED", "You cannot block yourself.");

        await RequireTargetAsync(targetUserId);
        // The composite primary key arbitrates concurrent PUTs across API instances.
        // A repeated PUT preserves the original relationship and timestamp.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "UserBlocks" ("BlockerUserId", "BlockedUserId", "CreatedAt")
            VALUES ({currentUserId}, {targetUserId}, {DateTimeOffset.UtcNow})
            ON CONFLICT ("BlockerUserId", "BlockedUserId") DO NOTHING
            """);
    }

    public async Task UnblockAsync(Guid currentUserId, Guid targetUserId)
    {
        await RequireTargetAsync(targetUserId);
        // Only remove the caller's outgoing relationship, never the reverse block.
        await db.UserBlocks
            .Where(b => b.BlockerUserId == currentUserId && b.BlockedUserId == targetUserId)
            .ExecuteDeleteAsync();
    }

    public Task<bool> IsBlockedAsync(Guid userAId, Guid userBId) =>
        db.UserBlocks.AsNoTracking().AnyAsync(b =>
            (b.BlockerUserId == userAId && b.BlockedUserId == userBId) ||
            (b.BlockerUserId == userBId && b.BlockedUserId == userAId));

    public Task<bool> HasBlockedAsync(Guid blockerUserId, Guid blockedUserId) =>
        db.UserBlocks.AsNoTracking().AnyAsync(b =>
            b.BlockerUserId == blockerUserId && b.BlockedUserId == blockedUserId);

    private async Task RequireTargetAsync(Guid targetUserId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == targetUserId))
            throw new BlockException(404, "NOT_FOUND", "Target user does not exist.");
    }
}

public sealed class BlockException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
