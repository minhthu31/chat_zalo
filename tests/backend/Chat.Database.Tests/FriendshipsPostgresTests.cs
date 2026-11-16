using Chat.Application.Services;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

// Opt-in PostgreSQL integration test. Requires CHAT_TEST_CONNECTION_STRING pointing
// only to a dedicated *_test or *_review database; never targets chat_zalo.
public sealed class FriendshipsPostgresTests
{
    private static ChatDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var connectionString = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a dedicated PostgreSQL test database.");
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = parsed.Database ?? string.Empty;
        if (!databaseName.Contains("_test", StringComparison.OrdinalIgnoreCase)
            && !databaseName.Contains("_review", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The test database name must contain _test or _review.");

        var options = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(connectionString);
        if (interceptors.Length > 0)
            options.AddInterceptors(interceptors);
        return new ChatDbContext(options.Options);
    }

    [Fact]
    public async Task Request_Accept_Reject_Cancel_Remove_Lists_And_Authorization_FollowContract()
    {
        await using var db = CreateContext();
        await db.Database.OpenConnectionAsync();
        Assert.Equal(18, ((NpgsqlConnection)db.Database.GetDbConnection()).PostgreSqlVersion.Major);

        var alice = NewUser("alice");
        var bob = NewUser("bob");
        await db.Users.AddRangeAsync(alice, bob);
        await db.SaveChangesAsync();
        try
        {
            var service = new FriendshipService(db);

            var sent = await service.SendRequestAsync(alice.Id, bob.Id);
            Assert.Equal(bob.Id, sent.UserId);
            Assert.Equal("Pending", sent.Status);
            Assert.False(await service.AreFriendsAsync(alice.Id, bob.Id));

            var duplicate = await Assert.ThrowsAsync<FriendshipException>(() => service.SendRequestAsync(bob.Id, alice.Id));
            Assert.Equal(409, duplicate.StatusCode);
            Assert.Equal("FRIEND_REQUEST_ALREADY_EXISTS", duplicate.Code);

            var self = await Assert.ThrowsAsync<FriendshipException>(() => service.SendRequestAsync(alice.Id, alice.Id));
            Assert.Equal(400, self.StatusCode);
            var missingTarget = await Assert.ThrowsAsync<FriendshipException>(() => service.SendRequestAsync(alice.Id, Guid.NewGuid()));
            Assert.Equal(404, missingTarget.StatusCode);
            Assert.Equal("USER_NOT_FOUND", missingTarget.Code);

            var wrongRecipient = await Assert.ThrowsAsync<FriendshipException>(() => service.AcceptAsync(alice.Id, bob.Id));
            Assert.Equal(403, wrongRecipient.StatusCode);
            var wrongCancel = await Assert.ThrowsAsync<FriendshipException>(() => service.RemoveAsync(bob.Id, alice.Id));
            Assert.Equal(403, wrongCancel.StatusCode);

            var incomingForBob = await service.GetRequestsAsync(bob.Id);
            Assert.Single(incomingForBob.Incoming);
            Assert.Equal(alice.Id, incomingForBob.Incoming[0].UserId);
            Assert.Empty(incomingForBob.Outgoing);

            await service.AcceptAsync(bob.Id, alice.Id);
            Assert.True(await service.AreFriendsAsync(alice.Id, bob.Id));
            Assert.Equal(bob.Id, Assert.Single(await service.GetFriendsAsync(alice.Id)).UserId);

            var alreadyFriends = await Assert.ThrowsAsync<FriendshipException>(() => service.SendRequestAsync(alice.Id, bob.Id));
            Assert.Equal(409, alreadyFriends.StatusCode);

            await service.RemoveAsync(alice.Id, bob.Id);
            Assert.False(await service.AreFriendsAsync(alice.Id, bob.Id));

            // A terminal relationship can be reused; only one canonical pair row remains.
            await service.SendRequestAsync(bob.Id, alice.Id);
            var incomingForAlice = await service.GetRequestsAsync(alice.Id);
            Assert.Equal(bob.Id, Assert.Single(incomingForAlice.Incoming).UserId);
            await service.RejectAsync(alice.Id, bob.Id);
            Assert.False(await service.AreFriendsAsync(alice.Id, bob.Id));
            var notPending = await Assert.ThrowsAsync<FriendshipException>(() => service.AcceptAsync(alice.Id, bob.Id));
            Assert.Equal(409, notPending.StatusCode);

            await service.SendRequestAsync(alice.Id, bob.Id);
            await service.RemoveAsync(alice.Id, bob.Id); // sender cancels outgoing Pending
            Assert.False(await service.AreFriendsAsync(alice.Id, bob.Id));
            Assert.Equal(1, await db.Friendships.CountAsync(x =>
                (x.UserLowId == alice.Id && x.UserHighId == bob.Id) ||
                (x.UserLowId == bob.Id && x.UserHighId == alice.Id)));
            Assert.Equal("Cancelled", await db.Friendships
                .Where(x => x.UserLowId == (alice.Id.CompareTo(bob.Id) < 0 ? alice.Id : bob.Id)
                         && x.UserHighId == (alice.Id.CompareTo(bob.Id) < 0 ? bob.Id : alice.Id))
                .Select(x => x.Status)
                .SingleAsync());
        }
        finally
        {
            await db.Friendships.Where(x => x.UserLowId == alice.Id || x.UserHighId == alice.Id
                || x.UserLowId == bob.Id || x.UserHighId == bob.Id).ExecuteDeleteAsync();
            await db.Users.Where(x => x.Id == alice.Id || x.Id == bob.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task GroupInvitationEligibility_IsCheckedPerInviterAndOnlyForAcceptedPairs()
    {
        var alice = NewUser("alice_group_invite");
        var bob = NewUser("bob_group_invite");
        var charlie = NewUser("charlie_group_invite");

        await using (var setup = CreateContext())
        {
            await setup.Users.AddRangeAsync(alice, bob, charlie);
            await setup.SaveChangesAsync();
        }

        try
        {
            await using var db = CreateContext();
            var service = new FriendshipService(db);

            // A is friends with B, and B is friends with C. This authorizes
            // A -> B and B -> C independently; it must not imply A -> C.
            await service.SendRequestAsync(alice.Id, bob.Id);
            Assert.False(await service.AreFriendsAsync(alice.Id, bob.Id)); // Pending
            await service.AcceptAsync(bob.Id, alice.Id);
            Assert.True(await service.AreFriendsAsync(alice.Id, bob.Id));

            await service.SendRequestAsync(bob.Id, charlie.Id);
            Assert.False(await service.AreFriendsAsync(bob.Id, charlie.Id)); // Pending
            await service.AcceptAsync(charlie.Id, bob.Id);
            Assert.True(await service.AreFriendsAsync(bob.Id, charlie.Id));
            Assert.False(await service.AreFriendsAsync(alice.Id, charlie.Id));

            // The Sprint 1 friend list remains Accepted-only and is scoped to
            // the requesting user, not to every member of a group.
            Assert.Equal(bob.Id, Assert.Single(await service.GetFriendsAsync(alice.Id)).UserId);
            Assert.Equal(
                new[] { alice.Id, charlie.Id }.OrderBy(id => id),
                (await service.GetFriendsAsync(bob.Id)).Select(friend => friend.UserId).OrderBy(id => id));
        }
        finally
        {
            await using var cleanup = CreateContext();
            var userIds = new[] { alice.Id, bob.Id, charlie.Id };
            await cleanup.Friendships
                .Where(x => userIds.Contains(x.UserLowId) || userIds.Contains(x.UserHighId))
                .ExecuteDeleteAsync();
            await cleanup.Users.Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task RevokedFriendship_IsDeniedWhenCheckedImmediatelyBeforeGroupMemberAddition()
    {
        var inviter = NewUser("group_inviter_revoked");
        var invitee = NewUser("group_invitee_revoked");

        await using (var setup = CreateContext())
        {
            await setup.Users.AddRangeAsync(inviter, invitee);
            await setup.SaveChangesAsync();
        }

        try
        {
            await using var db = CreateContext();
            var service = new FriendshipService(db);

            await service.SendRequestAsync(inviter.Id, invitee.Id);
            await service.AcceptAsync(invitee.Id, inviter.Id);
            Assert.True(await service.AreFriendsAsync(inviter.Id, invitee.Id));

            // Friendship is revoked after an earlier eligibility check.
            await service.RemoveAsync(inviter.Id, invitee.Id);

            // The group-membership caller must re-check at the point of
            // addition; a previously successful check cannot be reused.
            Assert.False(await service.AreFriendsAsync(inviter.Id, invitee.Id));
            Assert.DoesNotContain(
                await service.GetFriendsAsync(inviter.Id),
                friend => friend.UserId == invitee.Id);
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.Friendships
                .Where(x => (x.UserLowId == inviter.Id && x.UserHighId == invitee.Id)
                         || (x.UserLowId == invitee.Id && x.UserHighId == inviter.Id))
                .ExecuteDeleteAsync();
            await cleanup.Users.Where(x => x.Id == inviter.Id || x.Id == invitee.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ConcurrentAcceptAndReject_FromIndependentContexts_OnlyOneTransitionWins()
    {
        var alice = NewUser("alice_race");
        var bob = NewUser("bob_race");

        await using (var setup = CreateContext())
        {
            await setup.Users.AddRangeAsync(alice, bob);
            await setup.SaveChangesAsync();
            await new FriendshipService(setup).SendRequestAsync(alice.Id, bob.Id);
        }

        try
        {
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<bool> RunAsync(Func<FriendshipService, Task> operation)
            {
                await using var independentContext = CreateContext();
                var service = new FriendshipService(independentContext);
                await start.Task;
                try
                {
                    await operation(service);
                    return true;
                }
                catch (FriendshipException)
                {
                    return false;
                }
            }

            var accept = RunAsync(service => service.AcceptAsync(bob.Id, alice.Id));
            var reject = RunAsync(service => service.RejectAsync(bob.Id, alice.Id));
            start.SetResult(true);

            var outcomes = await Task.WhenAll(accept, reject);
            Assert.Single(outcomes, succeeded => succeeded);

            await using var verify = CreateContext();
            var low = alice.Id.CompareTo(bob.Id) < 0 ? alice.Id : bob.Id;
            var high = alice.Id.CompareTo(bob.Id) < 0 ? bob.Id : alice.Id;
            var finalStatus = await verify.Friendships
                .Where(x => x.UserLowId == low && x.UserHighId == high)
                .Select(x => x.Status)
                .SingleAsync();
            Assert.Contains(finalStatus, new[] { "Accepted", "Rejected" });
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.Friendships
                .Where(x => (x.UserLowId == alice.Id && x.UserHighId == bob.Id)
                         || (x.UserLowId == bob.Id && x.UserHighId == alice.Id))
                .ExecuteDeleteAsync();
            await cleanup.Users.Where(x => x.Id == alice.Id || x.Id == bob.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task AcceptThenRemove_FromIndependentContexts_BothSucceedAndFinalStatusIsCancelled()
    {
        var alice = NewUser("alice_accept_remove");
        var bob = NewUser("bob_accept_remove");

        await using (var setup = CreateContext())
        {
            await setup.Users.AddRangeAsync(alice, bob);
            await setup.SaveChangesAsync();
            await new FriendshipService(setup).SendRequestAsync(alice.Id, bob.Id);
        }

        try
        {
            // Preserve the actual business ordering: recipient accepts first, then
            // either participant removes the now-accepted friendship. Both are valid.
            await using (var acceptContext = CreateContext())
                await new FriendshipService(acceptContext).AcceptAsync(bob.Id, alice.Id);

            await using (var removeContext = CreateContext())
                await new FriendshipService(removeContext).RemoveAsync(alice.Id, bob.Id);

            await using var verify = CreateContext();
            var (low, high) = Normalize(alice.Id, bob.Id);
            var finalStatus = await verify.Friendships
                .Where(x => x.UserLowId == low && x.UserHighId == high)
                .Select(x => x.Status)
                .SingleAsync();

            Assert.Equal("Cancelled", finalStatus);
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.Friendships
                .Where(x => (x.UserLowId == alice.Id && x.UserHighId == bob.Id)
                         || (x.UserLowId == bob.Id && x.UserHighId == alice.Id))
                .ExecuteDeleteAsync();
            await cleanup.Users.Where(x => x.Id == alice.Id || x.Id == bob.Id).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData("Accept")]
    [InlineData("Reject")]
    [InlineData("Remove")]
    public async Task StaleOperation_CannotAffectCancelledAndResentRequest(string operationName)
    {
        var alice = NewUser($"alice_stale_{operationName.ToLowerInvariant()}");
        var bob = NewUser($"bob_stale_{operationName.ToLowerInvariant()}");

        await using (var setup = CreateContext())
        {
            await setup.Users.AddRangeAsync(alice, bob);
            await setup.SaveChangesAsync();
            await new FriendshipService(setup).SendRequestAsync(alice.Id, bob.Id);
        }

        var gate = new PauseAfterFriendshipSnapshotInterceptor();
        Task? staleOperation = null;
        try
        {
            await using var staleContext = CreateContext(gate);
            var staleService = new FriendshipService(staleContext);
            staleOperation = operationName switch
            {
                "Accept" => staleService.AcceptAsync(bob.Id, alice.Id),
                "Reject" => staleService.RejectAsync(bob.Id, alice.Id),
                "Remove" => staleService.RemoveAsync(alice.Id, bob.Id),
                _ => throw new ArgumentOutOfRangeException(nameof(operationName))
            };

            // Pause after PostgreSQL returned the old request snapshot but before
            // the service can acquire FOR UPDATE.
            await gate.SnapshotRead.WaitAsync(TimeSpan.FromSeconds(15));

            DateTimeOffset replacementVersion;
            await using (var replacementContext = CreateContext())
            {
                var replacementService = new FriendshipService(replacementContext);
                await replacementService.RemoveAsync(alice.Id, bob.Id);
                await replacementService.SendRequestAsync(alice.Id, bob.Id);

                var (low, high) = Normalize(alice.Id, bob.Id);
                replacementVersion = await replacementContext.Friendships
                    .Where(x => x.UserLowId == low && x.UserHighId == high)
                    .Select(x => x.CreatedAt)
                    .SingleAsync();
            }

            gate.Continue();
            var staleError = await Assert.ThrowsAsync<FriendshipException>(() => staleOperation!);
            Assert.Equal(409, staleError.StatusCode);
            Assert.Contains("STATE_CONFLICT", staleError.Code, StringComparison.Ordinal);

            await using var verify = CreateContext();
            var (verifyLow, verifyHigh) = Normalize(alice.Id, bob.Id);
            var current = await verify.Friendships
                .Where(x => x.UserLowId == verifyLow && x.UserHighId == verifyHigh)
                .SingleAsync();

            Assert.Equal("Pending", current.Status);
            Assert.Equal(alice.Id, current.RequesterUserId);
            Assert.Equal(replacementVersion, current.CreatedAt);
        }
        finally
        {
            gate.Continue();
            if (staleOperation is not null)
            {
                try { await staleOperation; }
                catch (FriendshipException) { }
            }
            await using var cleanup = CreateContext();
            await cleanup.Friendships
                .Where(x => (x.UserLowId == alice.Id && x.UserHighId == bob.Id)
                         || (x.UserLowId == bob.Id && x.UserHighId == alice.Id))
                .ExecuteDeleteAsync();
            await cleanup.Users.Where(x => x.Id == alice.Id || x.Id == bob.Id).ExecuteDeleteAsync();
        }
    }

    private static (Guid Low, Guid High) Normalize(Guid a, Guid b) =>
        a.CompareTo(b) < 0 ? (a, b) : (b, a);

    private sealed class PauseAfterFriendshipSnapshotInterceptor : DbCommandInterceptor
    {
        private int _paused;
        private readonly TaskCompletionSource _snapshotRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _continue =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SnapshotRead => _snapshotRead.Task;

        public void Continue() => _continue.TrySetResult();

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText;
            if (sql.Contains("\"Friendships\"", StringComparison.Ordinal)
                && !sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Exchange(ref _paused, 1) == 0)
            {
                _snapshotRead.TrySetResult();
                await _continue.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private static ApplicationUser NewUser(string prefix)
    {
        var id = Guid.NewGuid();
        var username = $"{prefix}_{id:N}";
        return new ApplicationUser
        {
            Id = id,
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            Email = $"{username}@example.test",
            NormalizedEmail = $"{username}@EXAMPLE.TEST".ToUpperInvariant(),
            DisplayName = prefix
        };
    }
}
