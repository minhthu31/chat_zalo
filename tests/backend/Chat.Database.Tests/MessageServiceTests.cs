using Chat.Application.DTOs.Messages;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

public sealed class MessageServiceTests
{
    private static ChatDbContext CreateContext()
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
            !database.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Chỉ chạy bộ test này trên database test riêng.");
        }

        return new ChatDbContext(
            new DbContextOptionsBuilder<ChatDbContext>()
                .UseNpgsql(connectionString)
                .Options);
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
        string body)
    {
        // Mỗi request dùng một DbContext riêng, giống request HTTP.
        await using var db = CreateContext();

        return await new MessageService(db).SendAsync(
            userId,
            conversationId,
            new SendMessageRequest
            {
                ClientMessageId = clientMessageId,
                Body = body
            });
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

        Assert.All(responses, response =>
            Assert.True(response.IsNewlyCreated));

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

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ =>
                SendAsync(a, conversationId, clientId, "same body")));

        Assert.Single(responses.Where(r => r.IsNewlyCreated));
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

        await SendAsync(a, conversationId, clientId, "original");

        var error = await Assert.ThrowsAsync<MessageServiceException>(() =>
            SendAsync(a, conversationId, clientId, "changed"));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("CLIENT_MESSAGE_ID_CONFLICT", error.Code);

        await using var verify = CreateContext();

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
                a, conversationId, Guid.NewGuid(), $"message-{i}");
        }

        await using var db = CreateContext();
        var service = new MessageService(db);

        var latest = await service.GetHistoryAsync(
            a, conversationId, null, 2);

        var before = await service.GetHistoryAsync(
            a, conversationId, 5, 2);

        var after = await service.GetNewMessagesAsync(
            a, conversationId, 2, 2);

        Assert.Equal(new long[] { 5, 6 }, latest.Select(m => m.Seq));
        Assert.Equal(new long[] { 3, 4 }, before.Select(m => m.Seq));
        Assert.Equal(new long[] { 3, 4 }, after.Select(m => m.Seq));
    }

    [Fact]
    public async Task Block_PreventsSendingAndBlockedUserReading()
    {
        var (a, b, conversationId) = await SeedAsync();

        await SendAsync(a, conversationId, Guid.NewGuid(), "old message");

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
                    userId, conversationId, Guid.NewGuid(), "blocked"));

            Assert.Equal(403, error.StatusCode);
        }

        await using var readDb = CreateContext();
        var service = new MessageService(readDb);

        Assert.Single(await service.GetHistoryAsync(
            a, conversationId, null));

        var readError = await Assert.ThrowsAsync<MessageServiceException>(
            () => service.GetHistoryAsync(b, conversationId, null));

        Assert.Equal(403, readError.StatusCode);
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
                    .SetProperty(m => m.LeftAt, DateTimeOffset.UtcNow));
        }

        var sendError = await Assert.ThrowsAsync<MessageServiceException>(
            () => SendAsync(a, conversationId, Guid.NewGuid(), "denied"));

        Assert.Equal(403, sendError.StatusCode);

        await using var readDb = CreateContext();

        var readError = await Assert.ThrowsAsync<MessageServiceException>(
            () => new MessageService(readDb)
                .GetHistoryAsync(a, conversationId, null));

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
            () => SendAsync(a, conversationId, Guid.NewGuid(), "denied"));

        Assert.Equal(403, error.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task InvalidBody_IsRejected(string? body)
    {
        var (a, _, conversationId) = await SeedAsync();

        var error = await Assert.ThrowsAsync<MessageServiceException>(
            () => SendAsync(a, conversationId, Guid.NewGuid(), body!));

        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task OversizedBodyAndEmptyClientId_AreRejected()
    {
        var (a, _, conversationId) = await SeedAsync();

        var oversized = await Assert.ThrowsAsync<MessageServiceException>(
            () => SendAsync(
                a, conversationId, Guid.NewGuid(), new string('x', 4001)));

        var emptyId = await Assert.ThrowsAsync<MessageServiceException>(
            () => SendAsync(a, conversationId, Guid.Empty, "hello"));

        Assert.Equal(400, oversized.StatusCode);
        Assert.Equal(400, emptyId.StatusCode);
    }
}