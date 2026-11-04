using Chat.Application.Services;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

// Opt-in PostgreSQL integration test. Requires CHAT_TEST_CONNECTION_STRING pointing
// only to a dedicated *_test or *_review database; never targets chat_zalo.
public sealed class FriendshipsPostgresTests
{
    private static ChatDbContext CreateContext()
    {
        var connectionString = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a dedicated PostgreSQL test database.");
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        if (!parsed.Database.Contains("_test", StringComparison.OrdinalIgnoreCase)
            && !parsed.Database.Contains("_review", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The test database name must contain _test or _review.");
        return new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(connectionString).Options);
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
            var notPending = await Assert.ThrowsAsync<FriendshipException>(() => service.AcceptAsync(alice.Id, bob.Id));
            Assert.Equal(409, notPending.StatusCode);

            await service.SendRequestAsync(alice.Id, bob.Id);
            await service.RemoveAsync(alice.Id, bob.Id); // sender cancels outgoing Pending
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
    public async Task ConcurrentAcceptAndRemove_FromIndependentContexts_DoNotOverwriteEachOther()
    {
        var alice = NewUser("alice_remove_race");
        var bob = NewUser("bob_remove_race");

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
            var remove = RunAsync(service => service.RemoveAsync(alice.Id, bob.Id));
            start.SetResult(true);

            var outcomes = await Task.WhenAll(accept, remove);
            Assert.Single(outcomes, succeeded => succeeded);

            await using var verify = CreateContext();
            var low = alice.Id.CompareTo(bob.Id) < 0 ? alice.Id : bob.Id;
            var high = alice.Id.CompareTo(bob.Id) < 0 ? bob.Id : alice.Id;
            var finalStatus = await verify.Friendships
                .Where(x => x.UserLowId == low && x.UserHighId == high)
                .Select(x => x.Status)
                .SingleAsync();
            Assert.Contains(finalStatus, new[] { "Accepted", "Cancelled" });
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
