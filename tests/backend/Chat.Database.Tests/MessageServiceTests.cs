using Chat.Infrastructure.Services;
using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

public sealed class MessageServiceTests
{
    private sealed class NullPublisher : IConversationEventPublisher
    {
        public static readonly NullPublisher Instance = new();

        public Task NotifyConversationChangedAsync(
            Guid conversationId,
            Guid eventId,
            long latestSeq) => Task.CompletedTask;
    }

    private sealed record PublishedEvent(
        Guid ConversationId,
        Guid EventId,
        long LatestSeq,
        bool MessageWasCommitted);

    private sealed class RecordingPublisher : IConversationEventPublisher
    {
        public List<PublishedEvent> Events { get; } = [];

        public async Task NotifyConversationChangedAsync(
            Guid conversationId,
            Guid eventId,
            long latestSeq)
        {
            await using var verify = CreateContext();
            var committed = await verify.Messages.AnyAsync(message =>
                message.ConversationId == conversationId &&
                message.Seq == latestSeq);
            Events.Add(new PublishedEvent(
                conversationId, eventId, latestSeq, committed));
        }
    }

    private sealed class ThrowingPublisher : IConversationEventPublisher
    {
        public int Calls { get; private set; }
        public bool MessageWasCommittedWhenCalled { get; private set; }

        public async Task NotifyConversationChangedAsync(
            Guid conversationId,
            Guid eventId,
            long latestSeq)
        {
            Calls++;
            await using var verify = CreateContext();
            MessageWasCommittedWhenCalled = await verify.Messages.AnyAsync(message =>
                message.ConversationId == conversationId &&
                message.Seq == latestSeq);
            throw new InvalidOperationException("SIMULATED_SIGNALR_PUBLISH_FAILURE");
        }
    }

        private static MessageService CreateService(
        ChatDbContext db,
        IConversationEventPublisher? publisher = null) =>
        new(
            db,
            new BlockService(db),
            new Chat.Application.Services.GroupPermissionService(
                new Chat.Infrastructure.Repositories.GroupMembershipQuery(db),
                new Chat.Infrastructure.Services.FriendshipService(db)),
            publisher ?? NullPublisher.Instance,
            NullLogger<MessageService>.Instance);
    private static ChatDbContext CreateContext(
        SaveChangesInterceptor? interceptor = null)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Chưa cấu hình CHAT_TEST_CONNECTION_STRING.");
        }

        var database =
            new NpgsqlConnectionStringBuilder(connectionString).Database;

        if (database is null ||
            !database.Contains("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Chỉ chạy bộ test này trên database test riêng.");
        }

        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql(connectionString);

        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new ChatDbContext(options.Options);
    }

    private static async Task<(Guid A, Guid B, Guid ConversationId)> SeedAsync()
    {
        await using var db = CreateContext();

        await db.Database.OpenConnectionAsync();

        Assert.Equal(
            18,
            ((NpgsqlConnection)db.Database.GetDbConnection())
                .PostgreSqlVersion.Major);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        foreach (var id in new[] { a, b })
        {
            var name = $"msg_{id:N}";

            db.Users.Add(new ApplicationUser
            {
                Id = id,
                UserName = name,
                NormalizedUserName = name.ToUpperInvariant(),
                Email = $"{name}@example.test",
                NormalizedEmail = $"{name}@example.test".ToUpperInvariant(),
                DisplayName = name,
                SecurityStamp = Guid.NewGuid().ToString()
            });
        }

        await db.SaveChangesAsync();

        var aFirst = string.Compare(
            a.ToString("D"),
            b.ToString("D"),
            StringComparison.Ordinal) < 0;

        db.Friendships.Add(new Friendship
        {
            UserLowId = aFirst ? a : b,
            UserHighId = aFirst ? b : a,
            RequesterUserId = a,
            Status = "Accepted"
        });

        await db.SaveChangesAsync();

        var conversations = new ConversationService(
            db,
            new FriendshipService(db),
            new BlockService(db));

        var conversation = await conversations.CreateDirectAsync(a, b);

        return (a, b, conversation.Id);
    }

    private static async Task<MessageResponse> SendAsync(
        Guid userId,
        Guid conversationId,
        Guid clientMessageId,
        string body,
        IConversationEventPublisher? publisher = null)
    {
        // Mỗi request dùng một DbContext riêng.
        await using var db = CreateContext();

        return await CreateService(db, publisher).SendAsync(
            userId,
            conversationId,
            new SendMessageRequest
            {
                ClientMessageId = clientMessageId,
                Body = body
            });
    }

    [Fact]
    public async Task NewMessage_PublishesOnlyAfterCommit_AndRetryDoesNotPublishAgain()
    {
        var (sender, _, conversationId) = await SeedAsync();
        var publisher = new RecordingPublisher();
        var clientMessageId = Guid.NewGuid();

        var created = await SendAsync(
            sender,
            conversationId,
            clientMessageId,
            "publish once",
            publisher);

        Assert.True(created.IsNewlyCreated);
        var published = Assert.Single(publisher.Events);
        Assert.Equal(conversationId, published.ConversationId);
        Assert.Equal(created.Seq, published.LatestSeq);
        Assert.NotEqual(Guid.Empty, published.EventId);
        Assert.True(published.MessageWasCommitted);

        var retry = await SendAsync(
            sender,
            conversationId,
            clientMessageId,
            "publish once",
            publisher);

        Assert.False(retry.IsNewlyCreated);
        Assert.Equal(created.Id, retry.Id);
        Assert.Single(publisher.Events);
    }

    [Fact]
    public async Task PublisherFailureAfterCommit_DoesNotFailSendOrRollbackMessage()
    {
        var (sender, _, conversationId) = await SeedAsync();
        var publisher = new ThrowingPublisher();
        var clientMessageId = Guid.NewGuid();

        var sent = await SendAsync(
            sender,
            conversationId,
            clientMessageId,
            "persist despite notification failure",
            publisher);

        Assert.True(sent.IsNewlyCreated);
        Assert.True(publisher.MessageWasCommittedWhenCalled);
        Assert.Equal(1, publisher.Calls);

        await using (var verify = CreateContext())
        {
            Assert.True(await verify.Messages.AnyAsync(message => message.Id == sent.Id));
            Assert.Equal(
                sent.Seq,
                await verify.Conversations
                    .Where(conversation => conversation.Id == conversationId)
                    .Select(conversation => conversation.LastMessageSeq)
                    .SingleAsync());
        }

        // Retry returns the committed message and does not re-run a failed publisher.
        var retry = await SendAsync(
            sender,
            conversationId,
            clientMessageId,
            "persist despite notification failure",
            publisher);
        Assert.False(retry.IsNewlyCreated);
        Assert.Equal(sent.Id, retry.Id);
        Assert.Equal(1, publisher.Calls);
    }

    [Fact]
    public async Task ConcurrentSends_CreateUniqueContinuousSequences()
    {
        var (a, b, conversationId) = await SeedAsync();

        var start = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, 20).Select(async index =>
        {
            await start.Task;

            return await SendAsync(
                index % 2 == 0 ? a : b,
                conversationId,
                Guid.NewGuid(),
                $"message-{index}");
        }).ToArray();

        start.SetResult(true);

        var responses = await Task.WhenAll(tasks);

        Assert.All(
            responses,
            response => Assert.True(response.IsNewlyCreated));

        Assert.Equal(
            Enumerable.Range(1, 20).Select(n => (long)n),
            responses.Select(r => r.Seq).OrderBy(seq => seq));

        await using var verify = CreateContext();

        Assert.Equal(
            20,
            await verify.Messages.CountAsync(m =>
                m.ConversationId == conversationId));

        Assert.Equal(
            20L,
            await verify.Conversations
                .Where(c => c.Id == conversationId)
                .Select(c => c.LastMessageSeq)
                .SingleAsync());
    }

    [Fact]
    public async Task ConcurrentRetries_CreateExactlyOneMessage()
    {
        var (a, _, conversationId) = await SeedAsync();
        var clientId = Guid.NewGuid();

        var start = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            await start.Task;

            return await SendAsync(
                a,
                conversationId,
                clientId,
                "same body");
        }).ToArray();

        start.SetResult(true);

        var responses = await Task.WhenAll(tasks);

        Assert.Single(responses, response => response.IsNewlyCreated);
        Assert.Single(responses.Select(r => r.Id).Distinct());

        await using var verify = CreateContext();

        Assert.Equal(
            1,
            await verify.Messages.CountAsync(m =>
                m.ConversationId == conversationId));

        Assert.Equal(
            1L,
            await verify.Conversations
                .Where(c => c.Id == conversationId)
                .Select(c => c.LastMessageSeq)
                .SingleAsync());
    }

    [Fact]
    public async Task RetryWithDifferentBody_ReturnsConflictWithoutIncrement()
    {
        var (a, _, conversationId) = await SeedAsync();
        var clientId = Guid.NewGuid();

        await SendAsync(
            a,
            conversationId,
            clientId,
            "original");

        var error = await Assert.ThrowsAsync<MessageServiceException>(
            () => SendAsync(
                a,
                conversationId,
                clientId,
                "changed"));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("CLIENT_MESSAGE_ID_CONFLICT", error.Code);

        await using var verify = CreateContext();

        Assert.Equal(
            1,
            await verify.Messages.CountAsync(m =>
                m.ConversationId == conversationId));

        Assert.Equal(
            "original",
            await verify.Messages
                .Where(m => m.ConversationId == conversationId)
                .Select(m => m.Body)
                .SingleAsync());

        Assert.Equal(
            1L,
            await verify.Conversations
                .Where(c => c.Id == conversationId)
                .Select(c => c.LastMessageSeq)
                .SingleAsync());
    }

    [Fact]
    public async Task Pagination_ReturnsCorrectAscendingPages()
    {
        var (a, _, conversationId) = await SeedAsync();

        for (var i = 1; i <= 6; i++)
        {
            await SendAsync(
                a,
                conversationId,
                Guid.NewGuid(),
                $"message-{i}");
        }

        await using var db = CreateContext();
        var service = CreateService(db);

        var latest = await service.GetHistoryAsync(
            a,
            conversationId,
            null,
            2);

        var before = await service.GetHistoryAsync(
            a,
            conversationId,
            5,
            2);

        var after = await service.GetNewMessagesAsync(
            a,
            conversationId,
            2,
            2);

        Assert.Equal(
            new long[] { 5, 6 },
            latest.Select(m => m.Seq));

        Assert.Equal(
            new long[] { 3, 4 },
            before.Select(m => m.Seq));

        Assert.Equal(
            new long[] { 3, 4 },
            after.Select(m => m.Seq));
    }

    [Fact]
    public async Task Block_PreventsSendingAndBlockedUserReading()
    {
        var (a, b, conversationId) = await SeedAsync();

        await SendAsync(
            a,
            conversationId,
            Guid.NewGuid(),
            "old message");

        await using (var db = CreateContext())
        {
            db.UserBlocks.Add(new UserBlock
            {
                BlockerUserId = a,
                BlockedUserId = b
            });

            await db.SaveChangesAsync();
        }

        foreach (var userId in new[] { a, b })
        {
            var error = await Assert.ThrowsAsync<MessageServiceException>(
                () => SendAsync(
                    userId,
                    conversationId,
                    Guid.NewGuid(),
                    "blocked"));

            Assert.Equal(403, error.StatusCode);
            Assert.Equal("CONVERSATION_ACCESS_DENIED", error.Code);
        }

        await using var readDb = CreateContext();
        var service = CreateService(readDb);

        // Người chủ động chặn vẫn xem được lịch sử.
        Assert.Single(
            await service.GetHistoryAsync(
                a,
                conversationId,
                null));

        // Người bị chặn không xem được lịch sử.
        var readError = await Assert.ThrowsAsync<MessageServiceException>(
            () => service.GetHistoryAsync(
                b,
                conversationId,
                null));

        Assert.Equal(403, readError.StatusCode);
        Assert.Equal("CONVERSATION_ACCESS_DENIED", readError.Code);
    }

    [Fact]
    public async Task FormerMember_CannotSendOrRead()
    {
        var (a, _, conversationId) = await SeedAsync();

        await using (var db = CreateContext())
        {
            await db.ConversationMembers
                .Where(m =>
                    m.ConversationId == conversationId &&
                    m.UserId == a)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        m => m.LeftAt,
                        DateTimeOffset.UtcNow));
        }

        var sendError = await Assert.ThrowsAsync<MessageServiceException>(
            () => SendAsync(
                a,
                conversationId,
                Guid.NewGuid(),
                "denied"));

        Assert.Equal(403, sendError.StatusCode);

        await using var readDb = CreateContext();

        var readError = await Assert.ThrowsAsync<MessageServiceException>(
            () => CreateService(readDb)
                .GetHistoryAsync(
                    a,
                    conversationId,
                    null));

        Assert.Equal(403, readError.StatusCode);
    }

    [Fact]
    public async Task NotFriends_CannotSend()
    {
        var (a, b, conversationId) = await SeedAsync();

        await using (var db = CreateContext())
        {
            await db.Friendships
                .Where(f =>
                    (f.UserLowId == a && f.UserHighId == b) ||
                    (f.UserLowId == b && f.UserHighId == a))
                .ExecuteDeleteAsync();
        }

        var error = await Assert.ThrowsAsync<MessageServiceException>(
            () => SendAsync(
                a,
                conversationId,
                Guid.NewGuid(),
                "denied"));

        Assert.Equal(403, error.StatusCode);
        Assert.Equal("CONVERSATION_ACCESS_DENIED", error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task InvalidBody_IsRejected(string? body)
    {
        var (a, _, conversationId) = await SeedAsync();

        var error = await Assert.ThrowsAsync<MessageServiceException>(
            () => SendAsync(
                a,
                conversationId,
                Guid.NewGuid(),
                body!));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("VALIDATION_ERROR", error.Code);
    }

    [Fact]
    public async Task OversizedBodyAndEmptyClientId_AreRejected()
    {
        var (a, _, conversationId) = await SeedAsync();

        var oversized = await Assert.ThrowsAsync<MessageServiceException>(
            () => SendAsync(
                a,
                conversationId,
                Guid.NewGuid(),
                new string('x', 4001)));

        var emptyId = await Assert.ThrowsAsync<MessageServiceException>(
            () => SendAsync(
                a,
                conversationId,
                Guid.Empty,
                "hello"));

        Assert.Equal(400, oversized.StatusCode);
        Assert.Equal(400, emptyId.StatusCode);
    }

    [Fact]
    public async Task SaveFailure_RollsBackMessageAndSequence()
    {
        var (a, _, conversationId) = await SeedAsync();
        var publisher = new RecordingPublisher();

        // Tin đã lưu thành công phải còn nguyên sau rollback.
        var first = await SendAsync(
            a,
            conversationId,
            Guid.NewGuid(),
            "existing message",
            publisher);

        Assert.Single(publisher.Events);

        var failedClientId = Guid.NewGuid();

        var interceptor = new FailMessageSaveInterceptor(
            conversationId,
            expectedSequence: first.Seq + 1);

        await using (var failingDb = CreateContext(interceptor))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateService(failingDb, publisher).SendAsync(
                    a,
                    conversationId,
                    new SendMessageRequest
                    {
                        ClientMessageId = failedClientId,
                        Body = "this message must not be saved"
                    }));

            Assert.Equal(
                "SIMULATED_MESSAGE_SAVE_FAILURE",
                error.Message);

            Assert.True(interceptor.SequenceIncrementObserved);
        }

        Assert.Single(publisher.Events);

        // DbContext mới đọc trạng thái đã commit trong PostgreSQL.
        await using (var verify = CreateContext())
        {
            Assert.Equal(
                first.Seq,
                await verify.Conversations
                    .Where(c => c.Id == conversationId)
                    .Select(c => c.LastMessageSeq)
                    .SingleAsync());

            var messages = await verify.Messages
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .ToListAsync();

            var remaining = Assert.Single(messages);

            Assert.Equal(first.Id, remaining.Id);
            Assert.Equal("existing message", remaining.Body);

            Assert.False(
                await verify.Messages.AnyAsync(m =>
                    m.ConversationId == conversationId &&
                    m.ClientMessageId == failedClientId));
        }

        // Sau rollback vẫn gửi tiếp được và không mất Seq.
        var next = await SendAsync(
            a,
            conversationId,
            Guid.NewGuid(),
            "message after rollback",
            publisher);

        Assert.True(next.IsNewlyCreated);
        Assert.Equal(first.Seq + 1, next.Seq);
        Assert.Equal(2, publisher.Events.Count);
        Assert.All(publisher.Events, published => Assert.True(published.MessageWasCommitted));

        await using var finalVerify = CreateContext();

        Assert.Equal(
            2,
            await finalVerify.Messages.CountAsync(m =>
                m.ConversationId == conversationId));

        Assert.Equal(
            next.Seq,
            await finalVerify.Conversations
                .Where(c => c.Id == conversationId)
                .Select(c => c.LastMessageSeq)
                .SingleAsync());
    }


  private static async Task<(Guid A, Guid B, Guid C, Guid ConversationId)>
    SeedGroupAsync()
    {
        await using var db = CreateContext();

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        foreach (var id in new[] { a, b, c })
        {
            var name = $"group_msg_{id:N}";

            db.Users.Add(new ApplicationUser
            {
                Id = id,
                UserName = name,
                NormalizedUserName = name.ToUpperInvariant(),
                Email = $"{name}@example.test",
                NormalizedEmail = $"{name}@example.test".ToUpperInvariant(),
                DisplayName = name,
                SecurityStamp = Guid.NewGuid().ToString()
            });
        }

        var conversation = new Conversation
        {
            Id = conversationId,
            CreatedByUserId = a,
            Kind = "Group"
        };

        db.Conversations.Add(conversation);

        db.GroupConversations.Add(new GroupConversation
        {
            ConversationId = conversationId,
            Name = "CHAT-16 test group"
        });

        db.ConversationMembers.AddRange(
            new ConversationMember
            {
                ConversationId = conversationId,
                UserId = a,
                Role = "Admin"
            },
            new ConversationMember
            {
                ConversationId = conversationId,
                UserId = b,
                Role = "Member"
            },
            new ConversationMember
            {
                ConversationId = conversationId,
                UserId = c,
                Role = "Member"
            });

        await db.SaveChangesAsync();

        return (a, b, c, conversationId);
    }

    [Fact]
    public async Task DissolvedGroup_CannotSendMessages()
    {
        var (a, _, _, conversationId) = await SeedGroupAsync();

        await using (var db = CreateContext())
        {
            var group = await db.GroupConversations
                .SingleAsync(g => g.ConversationId == conversationId);

            group.DeletedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<MessageServiceException>(
            () => SendAsync(
                a,
                conversationId,
                Guid.NewGuid(),
                "Cannot send to dissolved group"));
    }

    [Fact]
    public async Task GroupBlock_HidesMessages_AllowsSending_PreservesMembership_AndUnblockRestoresHistory()
    {
        var (a, b, c, conversationId) = await SeedGroupAsync();

        await SendAsync(a, conversationId, Guid.NewGuid(), "A old message");
        await SendAsync(c, conversationId, Guid.NewGuid(), "C message");

        await using (var db = CreateContext())
        {
            await new BlockService(db).BlockAsync(a, b);
        }

        await using (var db = CreateContext())
        {
            var history = await CreateService(db).GetHistoryAsync(
                b, conversationId, null, 20);

            Assert.DoesNotContain(history, m => m.SenderUserId == a);
            Assert.Contains(history, m => m.SenderUserId == c);
        }

        var bMessage = await SendAsync(
            b, conversationId, Guid.NewGuid(), "B can still send");

        Assert.Equal(b, bMessage.SenderUserId);

        await using (var db = CreateContext())
        {
            var members = await db.ConversationMembers
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.UserId)
                .ToListAsync();

            Assert.Equal(3, members.Count);
            Assert.Contains(members, m => m.UserId == a && m.Role == "Admin" && m.LeftAt == null);
            Assert.Contains(members, m => m.UserId == b && m.Role == "Member" && m.LeftAt == null);
            Assert.Contains(members, m => m.UserId == c && m.Role == "Member" && m.LeftAt == null);
        }

        await using (var db = CreateContext())
        {
            await new BlockService(db).UnblockAsync(a, b);
        }

        await using (var db = CreateContext())
        {
            var history = await CreateService(db).GetHistoryAsync(
                b, conversationId, null, 20);

            Assert.Contains(history, m =>
                m.SenderUserId == a && m.Body == "A old message");

            Assert.Contains(history, m =>
                m.SenderUserId == c && m.Body == "C message");
        }
    }

    [Fact]
    public async Task GroupBlock_FiltersBeforePaginationLimit()
    {
        var (a, b, c, conversationId) = await SeedGroupAsync();

        await SendAsync(a, conversationId, Guid.NewGuid(), "A first");
        await SendAsync(c, conversationId, Guid.NewGuid(), "C second");
        await SendAsync(c, conversationId, Guid.NewGuid(), "C third");
        await SendAsync(a, conversationId, Guid.NewGuid(), "A fourth");

        await using (var db = CreateContext())
        {
            await new BlockService(db).BlockAsync(a, b);
        }

        await using (var db = CreateContext())
        {
            var history = await CreateService(db).GetHistoryAsync(
                b, conversationId, null, 1);

            var historyMessage = Assert.Single(history);
            Assert.Equal(c, historyMessage.SenderUserId);
            Assert.Equal("C third", historyMessage.Body);
        }

        await using (var db = CreateContext())
        {
            var newMessages = await CreateService(db).GetNewMessagesAsync(
                b, conversationId, 0, 1);

            var newMessage = Assert.Single(newMessages);
            Assert.Equal(c, newMessage.SenderUserId);
            Assert.Equal("C second", newMessage.Body);
        }
    }
    private sealed class FailMessageSaveInterceptor : SaveChangesInterceptor
    {
        private readonly Guid _conversationId;
        private readonly long _expectedSequence;

        public bool SequenceIncrementObserved { get; private set; }

        public FailMessageSaveInterceptor(
            Guid conversationId,
            long expectedSequence)
        {
            _conversationId = conversationId;
            _expectedSequence = expectedSequence;
        }

        public override async ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            var db = (ChatDbContext)eventData.Context!;

            var hasNewMessage = db.ChangeTracker
                .Entries<Message>()
                .Any(entry =>
                    entry.State == EntityState.Added &&
                    entry.Entity.ConversationId == _conversationId);

            if (!hasNewMessage)
            {
                return result;
            }

            Assert.NotNull(db.Database.CurrentTransaction);

            // ExecuteUpdateAsync đã tăng Seq trước SaveChangesAsync.
            // Truy vấn này đọc giá trị trong chính transaction đó.
            var sequence = await db.Conversations
                .AsNoTracking()
                .Where(c => c.Id == _conversationId)
                .Select(c => c.LastMessageSeq)
                .SingleAsync(cancellationToken);

            Assert.Equal(_expectedSequence, sequence);

            SequenceIncrementObserved = true;

            // Cố tình gây lỗi để kiểm tra rollback.
            throw new InvalidOperationException(
                "SIMULATED_MESSAGE_SAVE_FAILURE");
        }
    }
}
