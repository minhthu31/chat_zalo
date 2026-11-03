using Chat.Application.Services;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

// Opt in only after verifying a dedicated PostgreSQL 18 test database with CHAT-1 schema.
// No database creation, migrations, resets or modification of pre-existing rows.
public class BlocksPostgresTests
{
    private static ChatDbContext Context()
    {
        var cs = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("A verified PostgreSQL test connection is required.");
        var connection = new NpgsqlConnectionStringBuilder(cs);
        if (!string.Equals(
        connection.Database,
        "chat_zalo_review",
        StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only chat_zalo_review database is allowed for testing.");
        }
        return new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(cs).Options);
    }

    [Fact]
    public async Task Lifecycle_Direction_Ownership_MissingTarget_AndDuplicate()
    {
        await using var db = Context();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedAsync(db, a, b);
        try
        {
            var service = new BlockService(db);
            var missing = await Assert.ThrowsAsync<BlockException>(() => service.BlockAsync(a, Guid.NewGuid()));
            Assert.Equal(404, missing.StatusCode);
            missing = await Assert.ThrowsAsync<BlockException>(() => service.UnblockAsync(a, Guid.NewGuid()));
            Assert.Equal(404, missing.StatusCode);
            await service.BlockAsync(a, b);
            var createdAt = await db.UserBlocks.Where(x => x.BlockerUserId == a && x.BlockedUserId == b).Select(x => x.CreatedAt).SingleAsync();
            await service.BlockAsync(a, b);
            Assert.Equal(createdAt, await db.UserBlocks.Where(x => x.BlockerUserId == a && x.BlockedUserId == b).Select(x => x.CreatedAt).SingleAsync());
            Assert.True(await service.IsBlockedAsync(a, b));
            Assert.True(await service.IsBlockedAsync(b, a));
            Assert.True(await service.HasBlockedAsync(a, b));
            Assert.False(await service.HasBlockedAsync(b, a));
            await service.UnblockAsync(b, a);
            Assert.True(await service.HasBlockedAsync(a, b));
            await service.BlockAsync(b, a);
            await service.UnblockAsync(a, b);
            Assert.False(await service.HasBlockedAsync(a, b));
            Assert.True(await service.HasBlockedAsync(b, a));
            await service.UnblockAsync(b, a);
            await service.UnblockAsync(b, a);
            Assert.False(await service.IsBlockedAsync(a, b));
        }
        finally { await CleanupAsync(db, a, b); }
    }

    [Fact]
    public async Task ConcurrentPuts_WithIndependentContexts_CreateOneRelationship()
    {
        await using var db = Context();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedAsync(db, a, b);
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task PutAsync()
            {
                await using var worker = Context();
                await gate.Task;
                await new BlockService(worker).BlockAsync(a, b);
            }
            var first = PutAsync();
            var second = PutAsync();
            gate.SetResult();
            await Task.WhenAll(first, second);
            Assert.Equal(1, await db.UserBlocks.CountAsync(x => x.BlockerUserId == a && x.BlockedUserId == b));
        }
        finally { await CleanupAsync(db, a, b); }
    }

    private static async Task SeedAsync(ChatDbContext db, Guid a, Guid b)
    {
        await db.Database.OpenConnectionAsync();
        if (((NpgsqlConnection)db.Database.GetDbConnection()).PostgreSqlVersion.Major != 18)
            throw new InvalidOperationException("PostgreSQL 18 is required.");
        foreach (var id in new[] { a, b })
            db.Users.Add(new ApplicationUser { Id = id, UserName = $"blocks_{id:N}", NormalizedUserName = $"BLOCKS_{id:N}", NormalizedEmail = $"BLOCKS_{id:N}@TEST", DisplayName = "Blocks test" });
        await db.SaveChangesAsync();
    }

    private static async Task CleanupAsync(ChatDbContext db, Guid a, Guid b)
    {
        await db.UserBlocks.Where(x => (x.BlockerUserId == a && x.BlockedUserId == b) || (x.BlockerUserId == b && x.BlockedUserId == a)).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == a || x.Id == b).ExecuteDeleteAsync();
    }
}
