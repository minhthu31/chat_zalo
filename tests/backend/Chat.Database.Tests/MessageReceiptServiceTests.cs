using Chat.Application.DTOs.Receipts;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Chat.Infrastructure.Repositories;
using Chat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

public sealed class MessageReceiptServiceTests
{
    private sealed class NullPublisher : IReceiptEventPublisher
    {
        public Task PublishAsync(
            MessageReceiptChangedDto receipt,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CommitCheckingPublisher : IReceiptEventPublisher
    {
        public List<MessageReceiptChangedDto> Events { get; } = [];
        public List<(long Delivered, long Read)> PersistedCursors { get; } = [];

        public async Task PublishAsync(
            MessageReceiptChangedDto receipt,
            CancellationToken cancellationToken = default)
        {
            await using var verify = CreateContext();
            var member = await verify.ConversationMembers.AsNoTracking()
                .SingleAsync(m =>
                    m.ConversationId == receipt.ConversationId &&
                    m.UserId == receipt.UserId,
                    cancellationToken);

            PersistedCursors.Add((member.LastDeliveredSeq, member.LastReadSeq));
            Events.Add(receipt);
        }
    }

    private sealed record Fixture(
        Guid ConversationId,
        Guid FirstUserId,
        Guid SecondUserId,
        Guid? ThirdUserId = null);

    private static string ConnectionString
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
                ?? throw new InvalidOperationException(
                    "Set CHAT_TEST_CONNECTION_STRING to a dedicated migrated PostgreSQL test database.");

            var database = new NpgsqlConnectionStringBuilder(value).Database ?? string.Empty;
            if (!database.Contains("_test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Receipt tests require a dedicated database with _test in its name.");

            return value;
        }
    }

    private static ChatDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    private static async Task EnsurePostgreSql18Async()
    {
        await using var db = CreateContext();
        await db.Database.OpenConnectionAsync();
        Assert.Equal(
            18,
            ((NpgsqlConnection)db.Database.GetDbConnection()).PostgreSqlVersion.Major);
    }

    private static MessageReceiptService CreateService(
        ChatDbContext db,
        IReceiptEventPublisher? publisher = null) =>
        new(
            db,
            new GroupPermissionService(
                new GroupMembershipQuery(db),
                new FriendshipService(db)),
            publisher ?? new NullPublisher(),
            NullLogger<MessageReceiptService>.Instance);

    private static async Task<Guid> AddUserAsync(ChatDbContext db, string prefix)
    {
        var id = Guid.NewGuid();
        var username = $"{prefix}_{id:N}";
        db.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            Email = $"{username}@example.test",
            NormalizedEmail = $"{username}@EXAMPLE.TEST".ToUpperInvariant(),
            DisplayName = username,
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Fixture> SeedDirectAsync()
    {
        await EnsurePostgreSql18Async();
        await using var db = CreateContext();
        var first = await AddUserAsync(db, "receipt_direct");
        var second = await AddUserAsync(db, "receipt_direct");

        var (low, high) = string.Compare(
            first.ToString("D"), second.ToString("D"), StringComparison.Ordinal) < 0
            ? (first, second)
            : (second, first);

        db.Friendships.Add(new Friendship
        {
            UserLowId = low,
            UserHighId = high,
            RequesterUserId = first,
            Status = "Accepted"
        });
        await db.SaveChangesAsync();

        var conversation = await new ConversationService(
            db,
            new FriendshipService(db),
            new BlockService(db))
            .CreateDirectAsync(first, second);

        return new Fixture(conversation.Id, first, second);
    }

    private static async Task<Fixture> SeedGroupAsync(long secondMemberJoinedSeq = 0)
    {
        await EnsurePostgreSql18Async();
        await using var db = CreateContext();
        var admin = await AddUserAsync(db, "receipt_group");
        var member = await AddUserAsync(db, "receipt_group");
        var third = await AddUserAsync(db, "receipt_group");
        var conversationId = Guid.NewGuid();

        db.Conversations.Add(new Conversation
        {
            Id = conversationId,
            CreatedByUserId = admin,
            Kind = "Group"
        });
        db.GroupConversations.Add(new GroupConversation
        {
            ConversationId = conversationId,
            Name = "Receipt test group"
        });
        db.ConversationMembers.AddRange(
            new ConversationMember
            {
                ConversationId = conversationId,
                UserId = admin,
                Role = "Admin"
            },
            new ConversationMember
            {
                ConversationId = conversationId,
                UserId = member,
                Role = "Member",
                JoinedSeq = secondMemberJoinedSeq
            },
            new ConversationMember
            {
                ConversationId = conversationId,
                UserId = third,
                Role = "Member"
            });

        await db.SaveChangesAsync();
        return new Fixture(conversationId, admin, member, third);
    }

    private static async Task SeedMessagesAsync(
        Guid conversationId,
        params (Guid SenderId, long Seq)[] messages)
    {
        await using var db = CreateContext();
        db.Messages.AddRange(messages.Select(item => new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            SenderUserId = item.SenderId,
            ClientMessageId = Guid.NewGuid(),
            Seq = item.Seq,
            Body = $"receipt-message-{item.Seq}",
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(item.Seq)
        }));

        await db.Conversations
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.LastMessageSeq, messages.Max(m => m.Seq)));

        await db.SaveChangesAsync();
    }

    private static async Task AddBlockAsync(Guid blockerId, Guid blockedId)
    {
        await using var db = CreateContext();
        db.UserBlocks.Add(new UserBlock
        {
            BlockerUserId = blockerId,
            BlockedUserId = blockedId
        });
        await db.SaveChangesAsync();
    }

    private static async Task CleanupAsync(Fixture fixture)
    {
        await using var db = CreateContext();
        var userIds = new List<Guid> { fixture.FirstUserId, fixture.SecondUserId };
        if (fixture.ThirdUserId.HasValue)
            userIds.Add(fixture.ThirdUserId.Value);

        await db.Friendships
            .Where(f => userIds.Contains(f.UserLowId) || userIds.Contains(f.UserHighId))
            .ExecuteDeleteAsync();
        await db.UserBlocks
            .Where(b => userIds.Contains(b.BlockerUserId) || userIds.Contains(b.BlockedUserId))
            .ExecuteDeleteAsync();
        await db.Conversations
            .Where(c => c.Id == fixture.ConversationId)
            .ExecuteDeleteAsync();
        await db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task DirectReceipts_AreMonotonic_AndReadAutomaticallyAdvancesDelivered()
    {
        var fixture = await SeedDirectAsync();
        try
        {
            await SeedMessagesAsync(
                fixture.ConversationId,
                (fixture.SecondUserId, 2),
                (fixture.FirstUserId, 3));

            await using (var db = CreateContext())
            {
                var delivered = await CreateService(db).MarkDeliveredAsync(
                    fixture.SecondUserId,
                    fixture.ConversationId,
                    new UpdateReceiptRequest { Seq = 2 });

                Assert.Equal(2L, delivered.DeliveredSeq);
                Assert.Equal(0L, delivered.ReadSeq);
            }

            await using (var db = CreateContext())
            {
                var read = await CreateService(db).MarkReadAsync(
                    fixture.SecondUserId,
                    fixture.ConversationId,
                    new UpdateReceiptRequest { Seq = 3 });

                Assert.Equal(3L, read.DeliveredSeq);
                Assert.Equal(3L, read.ReadSeq);
            }

            await using (var db = CreateContext())
            {
                var lower = await CreateService(db).MarkDeliveredAsync(
                    fixture.SecondUserId,
                    fixture.ConversationId,
                    new UpdateReceiptRequest { Seq = 1 });

                Assert.Equal(3L, lower.DeliveredSeq);
                Assert.Equal(3L, lower.ReadSeq);

                var receipts = await CreateService(db).GetAsync(
                    fixture.SecondUserId,
                    fixture.ConversationId);

                Assert.Equal(2, receipts.Items.Count);
                Assert.Contains(receipts.Items, item =>
                    item.UserId == fixture.SecondUserId &&
                    item.DeliveredSeq == 3 &&
                    item.ReadSeq == 3);
            }
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task SequenceZero_ReturnsCurrentCursorsWithoutUpdatingOrPublishing()
    {
        var fixture = await SeedDirectAsync();
        try
        {
            await SeedMessagesAsync(
                fixture.ConversationId,
                (fixture.FirstUserId, 1),
                (fixture.SecondUserId, 2));

            var publisher = new CommitCheckingPublisher();
            await using var db = CreateContext();
            var service = CreateService(db, publisher);

            var initial = await service.MarkReadAsync(
                fixture.SecondUserId,
                fixture.ConversationId,
                new UpdateReceiptRequest { Seq = 2 });

            Assert.Equal(2L, initial.DeliveredSeq);
            Assert.Equal(2L, initial.ReadSeq);
            Assert.Single(publisher.Events);

            var deliveredNoOp = await service.MarkDeliveredAsync(
                fixture.SecondUserId,
                fixture.ConversationId,
                new UpdateReceiptRequest { Seq = 0 });
            var readNoOp = await service.MarkReadAsync(
                fixture.SecondUserId,
                fixture.ConversationId,
                new UpdateReceiptRequest { Seq = 0 });

            Assert.Equal(2L, deliveredNoOp.DeliveredSeq);
            Assert.Equal(2L, deliveredNoOp.ReadSeq);
            Assert.Equal(2L, readNoOp.DeliveredSeq);
            Assert.Equal(2L, readNoOp.ReadSeq);
            Assert.Single(publisher.Events);

            await using var verify = CreateContext();
            var member = await verify.ConversationMembers.AsNoTracking()
                .SingleAsync(m =>
                    m.ConversationId == fixture.ConversationId &&
                    m.UserId == fixture.SecondUserId);
            Assert.Equal(2L, member.LastDeliveredSeq);
            Assert.Equal(2L, member.LastReadSeq);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task UpdateReloadsTrackedMemberAfterDatabaseLockBeforeApplyingMaxCursor()
    {
        var fixture = await SeedDirectAsync();
        try
        {
            await SeedMessagesAsync(
                fixture.ConversationId,
                (fixture.FirstUserId, 1),
                (fixture.SecondUserId, 2),
                (fixture.FirstUserId, 3));

            await using var staleContext = CreateContext();
            var trackedMember = await staleContext.ConversationMembers.SingleAsync(m =>
                m.ConversationId == fixture.ConversationId &&
                m.UserId == fixture.SecondUserId);
            Assert.Equal(0L, trackedMember.LastDeliveredSeq);
            Assert.Equal(0L, trackedMember.LastReadSeq);

            await using (var advancingContext = CreateContext())
            {
                var advanced = await CreateService(advancingContext).MarkReadAsync(
                    fixture.SecondUserId,
                    fixture.ConversationId,
                    new UpdateReceiptRequest { Seq = 3 });
                Assert.Equal(3L, advanced.DeliveredSeq);
                Assert.Equal(3L, advanced.ReadSeq);
            }

            // staleContext still tracks the original zero-valued entity.
            // The service must reload it after acquiring FOR UPDATE.
            var result = await CreateService(staleContext).MarkDeliveredAsync(
                fixture.SecondUserId,
                fixture.ConversationId,
                new UpdateReceiptRequest { Seq = 1 });

            Assert.Equal(3L, result.DeliveredSeq);
            Assert.Equal(3L, result.ReadSeq);
            Assert.Equal(3L, trackedMember.LastDeliveredSeq);
            Assert.Equal(3L, trackedMember.LastReadSeq);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task InvalidOrMissingMessageSequence_IsRejected()
    {
        var fixture = await SeedDirectAsync();
        try
        {
            await SeedMessagesAsync(
                fixture.ConversationId,
                (fixture.FirstUserId, 1),
                (fixture.FirstUserId, 3));

            await using var db = CreateContext();
            var service = CreateService(db);

            var negative = await Assert.ThrowsAsync<ReceiptServiceException>(() =>
                service.MarkDeliveredAsync(
                    fixture.SecondUserId,
                    fixture.ConversationId,
                    new UpdateReceiptRequest { Seq = -1 }));
            Assert.Equal(400, negative.StatusCode);
            Assert.Equal("INVALID_RECEIPT_SEQ", negative.Code);

            var missing = await Assert.ThrowsAsync<ReceiptServiceException>(() =>
                service.MarkDeliveredAsync(
                    fixture.SecondUserId,
                    fixture.ConversationId,
                    new UpdateReceiptRequest { Seq = 2 }));
            Assert.Equal(400, missing.StatusCode);
            Assert.Equal("INVALID_RECEIPT_SEQ", missing.Code);

            var beyondLast = await Assert.ThrowsAsync<ReceiptServiceException>(() =>
                service.MarkReadAsync(
                    fixture.SecondUserId,
                    fixture.ConversationId,
                    new UpdateReceiptRequest { Seq = 4 }));
            Assert.Equal("INVALID_RECEIPT_SEQ", beyondLast.Code);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task GroupReceipt_BlockThenUnblock_RequiresExplicitAckForOldMessage()
    {
        var fixture = await SeedGroupAsync();
        try
        {
            // A sends 1, C sends 2; B must be able to read C while A blocks B.
            await SeedMessagesAsync(fixture.ConversationId,
                (fixture.FirstUserId, 1), (fixture.ThirdUserId!.Value, 2));
            await AddBlockAsync(fixture.FirstUserId, fixture.SecondUserId);
            await using var db = CreateContext();
            var service = CreateService(db);
            var hidden = await Assert.ThrowsAsync<ReceiptServiceException>(() =>
                service.MarkReadAsync(fixture.SecondUserId, fixture.ConversationId,
                    new UpdateReceiptRequest { Seq = 1 }));
            Assert.Equal(403, hidden.StatusCode);

            var cRead = await service.MarkReadAsync(fixture.SecondUserId, fixture.ConversationId,
                new UpdateReceiptRequest { Seq = 2 });
            Assert.True(cRead.IsRead);
            Assert.Equal(2, cRead.Seq);

            await using (var unblockDb = CreateContext())
            {
                await unblockDb.UserBlocks.Where(b =>
                    b.BlockerUserId == fixture.FirstUserId && b.BlockedUserId == fixture.SecondUserId)
                    .ExecuteDeleteAsync();
            }
            var afterUnblock = await service.GetAsync(fixture.SecondUserId, fixture.ConversationId);
            var oldA = Assert.Single(afterUnblock.Messages.Where(m => m.Seq == 1));
            Assert.DoesNotContain(fixture.SecondUserId, oldA.ReadByUserIds);
            var oldC = Assert.Single(afterUnblock.Messages.Where(m => m.Seq == 2));
            Assert.Contains(fixture.SecondUserId, oldC.ReadByUserIds);

            var aRead = await service.MarkReadAsync(fixture.SecondUserId, fixture.ConversationId,
                new UpdateReceiptRequest { Seq = 1 });
            Assert.True(aRead.IsRead);
            var afterAck = await service.GetAsync(fixture.SecondUserId, fixture.ConversationId);
            Assert.Contains(fixture.SecondUserId,
                Assert.Single(afterAck.Messages.Where(m => m.Seq == 1)).ReadByUserIds);

            var retry = await service.MarkReadAsync(fixture.SecondUserId, fixture.ConversationId,
                new UpdateReceiptRequest { Seq = 1 });
            Assert.True(retry.IsRead);
            await using var verify = CreateContext();
            var messageId = await verify.Messages.Where(m => m.ConversationId == fixture.ConversationId && m.Seq == 1)
                .Select(m => m.Id).SingleAsync();
            Assert.Equal(1, await verify.MessageUserReceipts.CountAsync(r =>
                r.MessageId == messageId && r.UserId == fixture.SecondUserId));
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task DirectReceipts_AreDeniedWhenPeerHasBlockedCaller()
    {
        var fixture = await SeedDirectAsync();
        try
        {
            await SeedMessagesAsync(fixture.ConversationId, (fixture.FirstUserId, 1));
            await AddBlockAsync(fixture.SecondUserId, fixture.FirstUserId);

            await using var db = CreateContext();
            var error = await Assert.ThrowsAsync<ReceiptServiceException>(() =>
                CreateService(db).GetAsync(
                    fixture.FirstUserId,
                    fixture.ConversationId));

            Assert.Equal(403, error.StatusCode);
            Assert.Equal("CONVERSATION_ACCESS_DENIED", error.Code);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task GroupGetReceipts_HidesReceiptOfMemberWhoseMessagesAreHiddenByBlocks()
    {
        var fixture = await SeedGroupAsync();
        try
        {
            await SeedMessagesAsync(
                fixture.ConversationId,
                (fixture.FirstUserId, 1),
                (fixture.SecondUserId, 2),
                (fixture.ThirdUserId!.Value, 3));

            // The second member has blocked the third member. The third member
            // cannot view messages from the second member and should not receive
            // that member's receipt metadata either.
            await AddBlockAsync(fixture.SecondUserId, fixture.ThirdUserId!.Value);

            await using var db = CreateContext();
            var receipts = await CreateService(db).GetAsync(
                fixture.ThirdUserId.Value,
                fixture.ConversationId);

            Assert.DoesNotContain(receipts.Items, item => item.UserId == fixture.SecondUserId);
            Assert.Contains(receipts.Items, item => item.UserId == fixture.FirstUserId);
            Assert.Contains(receipts.Items, item => item.UserId == fixture.ThirdUserId.Value);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task GroupMemberCannotAcknowledgeMessagesBeforeJoinedSeq()
    {
        var fixture = await SeedGroupAsync(secondMemberJoinedSeq: 2);
        try
        {
            await SeedMessagesAsync(
                fixture.ConversationId,
                (fixture.FirstUserId, 1),
                (fixture.SecondUserId, 2));

            await using var db = CreateContext();
            var error = await Assert.ThrowsAsync<ReceiptServiceException>(() =>
                CreateService(db).MarkReadAsync(
                    fixture.SecondUserId,
                    fixture.ConversationId,
                    new UpdateReceiptRequest { Seq = 1 }));

            Assert.Equal("INVALID_RECEIPT_SEQ", error.Code);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task ReceiptEvent_IsPublishedOnlyAfterCommit_AndOnlyWhenCursorChanges()
    {
        var fixture = await SeedDirectAsync();
        try
        {
            await SeedMessagesAsync(fixture.ConversationId, (fixture.FirstUserId, 1));
            var publisher = new CommitCheckingPublisher();

            await using var db = CreateContext();
            var service = CreateService(db, publisher);

            var result = await service.MarkReadAsync(
                fixture.SecondUserId,
                fixture.ConversationId,
                new UpdateReceiptRequest { Seq = 1 });

            Assert.Equal(1L, result.DeliveredSeq);
            Assert.Equal(1L, result.ReadSeq);
            Assert.Single(publisher.Events);
            Assert.Equal((1L, 1L), Assert.Single(publisher.PersistedCursors));
            Assert.Equal(fixture.SecondUserId, publisher.Events[0].UserId);

            // An idempotent retry must not emit a duplicate realtime event.
            await service.MarkReadAsync(
                fixture.SecondUserId,
                fixture.ConversationId,
                new UpdateReceiptRequest { Seq = 1 });
            Assert.Single(publisher.Events);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Fact]
    public async Task ConcurrentReceiptUpdates_PreserveMonotonicCursorsAndInvariant()
    {
        var fixture = await SeedDirectAsync();
        try
        {
            await SeedMessagesAsync(
                fixture.ConversationId,
                (fixture.FirstUserId, 1),
                (fixture.SecondUserId, 2),
                (fixture.FirstUserId, 3),
                (fixture.SecondUserId, 4),
                (fixture.FirstUserId, 5));

            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var tasks = Enumerable.Range(1, 10).Select(async index =>
            {
                await start.Task;
                await using var db = CreateContext();
                var service = CreateService(db);
                var seq = (index % 5) + 1;

                return index % 2 == 0
                    ? await service.MarkReadAsync(
                        fixture.SecondUserId,
                        fixture.ConversationId,
                        new UpdateReceiptRequest { Seq = seq })
                    : await service.MarkDeliveredAsync(
                        fixture.SecondUserId,
                        fixture.ConversationId,
                        new UpdateReceiptRequest { Seq = seq });
            }).ToArray();

            start.SetResult(true);
            await Task.WhenAll(tasks);

            await using var verify = CreateContext();
            var member = await verify.ConversationMembers.AsNoTracking()
                .SingleAsync(m =>
                    m.ConversationId == fixture.ConversationId &&
                    m.UserId == fixture.SecondUserId);

            Assert.Equal(5L, member.LastDeliveredSeq);
            Assert.Equal(5L, member.LastReadSeq);
            Assert.True(member.LastReadSeq <= member.LastDeliveredSeq);
            Assert.True(member.LastDeliveredSeq <= 5);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }
}
