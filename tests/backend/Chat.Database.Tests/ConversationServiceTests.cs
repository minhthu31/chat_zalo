namespace Chat.Database.Tests;

using Chat.Application.DTOs.Conversations;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Integration tests for <see cref="ConversationService"/>.
/// Requires a running Postgres instance with the schema already migrated.
/// Set the environment variable CHAT_TEST_CONNECTION_STRING before running.
/// </summary>
public class ConversationServiceTests
{
    // -----------------------------------------------------------------------
    // Infrastructure helpers
    // -----------------------------------------------------------------------

    private static string ConnectionString => Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a dedicated migrated test database.");

    private static ChatDbContext CreateDbContext()
    {
        var database = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString).Database ?? "";
        if (!database.Contains("_test", StringComparison.OrdinalIgnoreCase)
            && !database.Contains("_review", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A dedicated test/review database is required.");
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ChatDbContext(options);
    }

    private static ApplicationUser MakeUser(string suffix) => new()
    {
        Id                   = Guid.NewGuid(),
        UserName             = $"u_{suffix}"[..Math.Min(15, $"u_{suffix}".Length)],
        NormalizedUserName   = $"U_{suffix}"[..Math.Min(15, $"U_{suffix}".Length)],
        DisplayName          = $"User {suffix}",
    };

    /// <summary>
    /// Creates two users, persists them, establishes an Accepted friendship, and
    /// returns a <see cref="ConversationService"/> that operates on a FRESH DbContext
    /// (no cached state).
    /// </summary>
    private static async Task<(ChatDbContext ctx, ConversationService svc, Guid userAId, Guid userBId)>
        SetupFriendPairAsync()
    {
        var sfx  = Guid.NewGuid().ToString("N")[..8];
        var userA = MakeUser($"a{sfx}");
        var userB = MakeUser($"b{sfx}");

        await using (var setup = CreateDbContext())
        {
            setup.Users.AddRange(userA, userB);

            // Normalise the friendship pair
            var (low, high) = string.Compare(
                userA.Id.ToString("D"), userB.Id.ToString("D"),
                StringComparison.OrdinalIgnoreCase) < 0
                ? (userA.Id, userB.Id)
                : (userB.Id, userA.Id);

            setup.Friendships.Add(new Friendship
            {
                UserLowId       = low,
                UserHighId      = high,
                RequesterUserId = userA.Id,
                Status          = "Accepted",
            });

            await setup.SaveChangesAsync();
        }

        var ctx = CreateDbContext();
        var svc = new ConversationService(ctx, new FriendshipService(ctx), new BlockService(ctx));
        return (ctx, svc, userA.Id, userB.Id);
    }

    // -----------------------------------------------------------------------
    // Test 1 – happy path: create new conversation (IsNewlyCreated == true)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateDirectAsync_WhenFriendsAndNotBlocked_ReturnsNewConversation()
    {
        var (ctx, svc, userAId, userBId) = await SetupFriendPairAsync();
        await using (ctx)
        {
            var dto = await svc.CreateDirectAsync(userAId, userBId);

            Assert.NotEqual(Guid.Empty, dto.Id);
            Assert.Equal("Direct", dto.Kind);
            Assert.Equal(0L, dto.LastMessageSeq);
            Assert.True(dto.IsNewlyCreated, "First call must return IsNewlyCreated = true");
        }
    }

    // -----------------------------------------------------------------------
    // Test 2a – idempotent: second call returns existing conversation (IsNewlyCreated == false)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateDirectAsync_CalledTwice_ReturnsSameConversation()
    {
        var (ctx, svc, userAId, userBId) = await SetupFriendPairAsync();
        await using (ctx)
        {
            var first  = await svc.CreateDirectAsync(userAId, userBId);
            var second = await svc.CreateDirectAsync(userAId, userBId);

            Assert.Equal(first.Id, second.Id);
            Assert.False(second.IsNewlyCreated, "Second call must return IsNewlyCreated = false");

            // Verify only ONE DirectConversation row exists in DB
            var (low, high) = string.Compare(
                userAId.ToString("D"), userBId.ToString("D"),
                StringComparison.OrdinalIgnoreCase) < 0
                ? (userAId, userBId) : (userBId, userAId);

            await using var verify = CreateDbContext();
            var count = await verify.DirectConversations.CountAsync(
                dc => dc.UserLowId == low && dc.UserHighId == high);
            Assert.Equal(1, count);
        }
    }

    // -----------------------------------------------------------------------
    // Test 2b – race condition: concurrent creates produce exactly 1 record
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateDirectAsync_ConcurrentCalls_ProduceExactlyOneConversation()
    {
        var (ctx, _, userAId, userBId) = await SetupFriendPairAsync();
        await ctx.DisposeAsync(); // We'll create fresh contexts per task

        // Fire two concurrent CreateDirectAsync calls
        var results = await Task.WhenAll(
            Task.Run(async () =>
            {
                await using var c = CreateDbContext();
                var s = new ConversationService(c, new FriendshipService(c), new BlockService(c));
                return await s.CreateDirectAsync(userAId, userBId);
            }),
            Task.Run(async () =>
            {
                await using var c = CreateDbContext();
                var s = new ConversationService(c, new FriendshipService(c), new BlockService(c));
                return await s.CreateDirectAsync(userAId, userBId);
            }));

        // Both tasks must return the SAME conversation ID
        Assert.Equal(results[0].Id, results[1].Id);

        // At most one of them is "newly created"; the other sees IsNewlyCreated = false
        var newCount = results.Count(r => r.IsNewlyCreated);
        Assert.Equal(1, newCount);

        // Only one row in DB
        var (low, high) = string.Compare(
            userAId.ToString("D"), userBId.ToString("D"),
            StringComparison.OrdinalIgnoreCase) < 0
            ? (userAId, userBId) : (userBId, userAId);

        await using var verify = CreateDbContext();
        var count = await verify.DirectConversations.CountAsync(
            dc => dc.UserLowId == low && dc.UserHighId == high);
        Assert.Equal(1, count);
        Assert.Equal(2, await verify.ConversationMembers.CountAsync(m => m.ConversationId == results[0].Id));
    }

    // -----------------------------------------------------------------------
    // Test 3a – self-chat rejected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateDirectAsync_WithSelf_ThrowsUnauthorizedAccessException()
    {
        await using var ctx = CreateDbContext();
        var svc = new ConversationService(ctx, new FriendshipService(ctx), new BlockService(ctx));

        var userId = Guid.NewGuid();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.CreateDirectAsync(userId, userId));
    }

    // -----------------------------------------------------------------------
    // Test 3b – not friends: rejected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateDirectAsync_WhenNotFriends_ThrowsUnauthorizedAccessException()
    {
        // Create two users with NO friendship record
        var sfx   = Guid.NewGuid().ToString("N")[..8];
        var userA = MakeUser($"nf_a{sfx}");
        var userB = MakeUser($"nf_b{sfx}");

        await using (var setup = CreateDbContext())
        {
            setup.Users.AddRange(userA, userB);
            await setup.SaveChangesAsync();
        }

        await using var ctx = CreateDbContext();
        var svc = new ConversationService(ctx, new FriendshipService(ctx), new BlockService(ctx));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.CreateDirectAsync(userA.Id, userB.Id));
    }

    // -----------------------------------------------------------------------
    // Test 3c – blocked: rejected (bidirectional)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateDirectAsync_WhenBlocked_ThrowsUnauthorizedAccessException()
    {
        var (ctx, svc, userAId, userBId) = await SetupFriendPairAsync();
        await using (ctx)
        {
            // userB blocks userA
            await using (var blockCtx = CreateDbContext())
            {
                blockCtx.UserBlocks.Add(new UserBlock
                {
                    BlockerUserId = userBId,
                    BlockedUserId = userAId,
                });
                await blockCtx.SaveChangesAsync();
            }

            // userA tries to start a conversation → must be rejected
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => svc.CreateDirectAsync(userAId, userBId));
        }
    }

    // -----------------------------------------------------------------------
    // Test 3d – target user does not exist (HTTP 404 NOT_FOUND)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateDirectAsync_WhenTargetUserDoesNotExist_ThrowsKeyNotFoundException()
    {
        var sfx = Guid.NewGuid().ToString("N")[..8];
        var userA = MakeUser($"nf_a{sfx}");

        await using (var setup = CreateDbContext())
        {
            setup.Users.Add(userA);
            await setup.SaveChangesAsync();
        }

        await using var ctx = CreateDbContext();
        var svc = new ConversationService(ctx, new FriendshipService(ctx), new BlockService(ctx));

        var nonExistentTargetId = Guid.NewGuid();
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => svc.CreateDirectAsync(userA.Id, nonExistentTargetId));

        Assert.Equal("NOT_FOUND", ex.Message);
    }

    // -----------------------------------------------------------------------
    // Test 4 – GetConversationsAsync only returns current user's conversations
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetConversationsAsync_OnlyReturnsCurrentUsersConversations()
    {
        // Create two independent friend pairs and one conversation each
        var (ctxA, svcA, userA1, userA2) = await SetupFriendPairAsync();
        var (ctxB, svcB, userB1, userB2) = await SetupFriendPairAsync();

        Guid convIdA, convIdB;

        await using (ctxA) { convIdA = (await svcA.CreateDirectAsync(userA1, userA2)).Id; }
        await using (ctxB) { convIdB = (await svcB.CreateDirectAsync(userB1, userB2)).Id; }

        // userA1's list must contain convIdA but NOT convIdB
        await using var verCtx = CreateDbContext();
        var querySvc = new ConversationService(verCtx, new FriendshipService(verCtx), new BlockService(verCtx));
        var listA = await querySvc.GetConversationsAsync(userA1);

        Assert.Contains(listA, c => c.Id == convIdA);
        Assert.DoesNotContain(listA, c => c.Id == convIdB);

        // userB1's list must contain convIdB but NOT convIdA
        await using var verCtx2 = CreateDbContext();
        var querySvc2 = new ConversationService(verCtx2, new FriendshipService(verCtx2), new BlockService(verCtx2));
        var listB = await querySvc2.GetConversationsAsync(userB1);

        Assert.Contains(listB, c => c.Id == convIdB);
        Assert.DoesNotContain(listB, c => c.Id == convIdA);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingAndList_ApplyDirectionalBlocks(bool reverse)
    {
        var (ctx, svc, a, b) = await SetupFriendPairAsync();
        await using (ctx)
        {
            var id = (await svc.CreateDirectAsync(a, b)).Id;
            var blocker = reverse ? b : a;
            var blocked = reverse ? a : b;
            var blocks = new BlockService(ctx);
            await blocks.BlockAsync(blocker, blocked);
            Assert.Contains(await svc.GetConversationsAsync(blocker), x => x.Id == id);
            Assert.DoesNotContain(await svc.GetConversationsAsync(blocked), x => x.Id == id);
            Assert.Equal(id, (await svc.CreateDirectAsync(blocker, blocked)).Id);
            var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.CreateDirectAsync(blocked, blocker));
            Assert.Equal("CONVERSATION_ACCESS_DENIED", error.Message);
            await blocks.UnblockAsync(blocker, blocked);
            Assert.Contains(await svc.GetConversationsAsync(blocked), x => x.Id == id);
        }
    }

    [Fact]
    public async Task ConcurrentOppositeCreates_WithBothInsertsReady_ReturnOneCompleteConversation()
    {
        var (setup, _, a, b) = await SetupFriendPairAsync();
        await setup.DisposeAsync();
        var gate = new InsertGate();
        async Task<ConversationDto> Create(Guid caller, Guid target)
        {
            await using var db = new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>()
                .UseNpgsql(ConnectionString).AddInterceptors(gate).Options);
            return await new ConversationService(db, new FriendshipService(db), new BlockService(db)).CreateDirectAsync(caller, target);
        }
        var results = await Task.WhenAll(Create(a, b), Create(b, a));
        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Single(results, r => r.IsNewlyCreated);
        await using var verify = CreateDbContext();
        Assert.Equal(1, await verify.Conversations.CountAsync(c => c.Id == results[0].Id));
        Assert.Equal(1, await verify.DirectConversations.CountAsync(c => c.ConversationId == results[0].Id));
        var members = await verify.ConversationMembers.Where(m => m.ConversationId == results[0].Id).Select(m => m.UserId).ToListAsync();
        Assert.Equal(2, members.Count);
        Assert.Contains(a, members); Assert.Contains(b, members);
        Assert.Equal(1, await verify.Conversations.CountAsync(c => c.CreatedByUserId == a || c.CreatedByUserId == b));
    }

    [Fact]
    public async Task CommitFailure_RollsBackConversationPairAndBothMemberships()
    {
        var (setup, _, a, b) = await SetupFriendPairAsync();
        await setup.DisposeAsync();
        await using var db = new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql(ConnectionString).AddInterceptors(new FailCommit()).Options);
        var service = new ConversationService(db, new FriendshipService(db), new BlockService(db));
        await Assert.ThrowsAsync<InjectedCommitException>(() => service.CreateDirectAsync(a, b));
        var id = db.ChangeTracker.Entries<Conversation>().Single().Entity.Id;
        await using var verify = CreateDbContext();
        Assert.False(await verify.Conversations.AnyAsync(c => c.Id == id));
        Assert.False(await verify.DirectConversations.AnyAsync(c => c.ConversationId == id));
        Assert.False(await verify.ConversationMembers.AnyAsync(c => c.ConversationId == id));
    }

    private sealed class InsertGate : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<DirectConversation>().Any(e => e.State == EntityState.Added))
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }

    private sealed class InjectedCommitException : Exception { }
    private sealed class FailCommit : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult result,
            CancellationToken cancellationToken = default) => throw new InjectedCommitException();
    }

    [Fact]
    public async Task ExistingConversation_RequiresActiveMembership()
    {
        var (ctx, svc, a, b) = await SetupFriendPairAsync();
        await using (ctx)
        {
            var id = (await svc.CreateDirectAsync(a, b)).Id;
            var member = await ctx.ConversationMembers.SingleAsync(m => m.ConversationId == id && m.UserId == a);
            member.LeftAt = DateTimeOffset.UtcNow;
            await ctx.SaveChangesAsync();
            Assert.DoesNotContain(await svc.GetConversationsAsync(a), x => x.Id == id);
            var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.CreateDirectAsync(a, b));
            Assert.Equal("CONVERSATION_ACCESS_DENIED", error.Message);
            ctx.ConversationMembers.Remove(member);
            await ctx.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.CreateDirectAsync(a, b));
        }
    }
}
