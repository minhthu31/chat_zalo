using Chat.Application.Interfaces;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Services;

// Operator-only CLI: there is deliberately no public cleanup HTTP endpoint.
public sealed class PendingFileCleanupService(ChatDbContext db, IFileStorage storage) : IPendingFileCleanupService
{
    public async Task<int> CleanupAsync(DateTimeOffset uploadedBefore, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000 || uploadedBefore > DateTimeOffset.UtcNow)
            throw new ArgumentOutOfRangeException(nameof(limit), "Use a past cutoff and a limit from 1 to 1000.");
        var ids = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Status == "Pending" && f.CreatedAt < uploadedBefore
                && !db.MessageAttachments.Any(a => a.StoredFileId == f.Id)
                && !db.Users.Any(u => u.AvatarFileId == f.Id))
            .OrderBy(f => f.CreatedAt).ThenBy(f => f.Id).Select(f => f.Id).Take(limit).ToListAsync(cancellationToken);
        var count = 0;
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var file = await db.StoredFiles.FromSqlInterpolated($"SELECT * FROM \"StoredFiles\" WHERE \"Id\" = {id} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            // A caller's DbContext may already track a stale Pending instance.
            // Refresh it while holding the row lock before touching physical content.
            if (file is not null) await db.Entry(file).ReloadAsync(cancellationToken);
            if (file is not null && file.Status == "Pending" && file.CreatedAt < uploadedBefore
                && !await db.MessageAttachments.AnyAsync(a => a.StoredFileId == id, cancellationToken)
                && !await db.Users.AnyAsync(u => u.AvatarFileId == id, cancellationToken))
            {
                await storage.DeleteAsync(file.StorageKey, cancellationToken);
                db.StoredFiles.Remove(file);
                await db.SaveChangesAsync(cancellationToken);
                count++;
            }
            await tx.CommitAsync(cancellationToken);
        }
        return count;
    }
}
