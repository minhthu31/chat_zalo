using Chat.Api.Hubs;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chat.Database.Tests;

// Relational permission tests on SQLite; transport is a recording test double.
// PostgreSQL and browser end-to-end verification remain separate checks.
public class ConversationNotificationsTests
{
    [Fact]
    public async Task Publish_RechecksMembershipAndBlocks_OnEveryEvent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var outsider = Guid.NewGuid();
        db.Users.AddRange(new[] { a, b, outsider }.Select(id => new ApplicationUser
        {
            Id = id, UserName = id.ToString(), NormalizedUserName = id.ToString().ToUpperInvariant()
        }));
        var conversation = new Conversation { CreatedByUserId = a, LastMessageSeq = 1 };
        db.Conversations.Add(conversation);
        var memberA = new ConversationMember { ConversationId = conversation.Id, UserId = a };
        db.ConversationMembers.AddRange(memberA,
            new ConversationMember { ConversationId = conversation.Id, UserId = b });
        await db.SaveChangesAsync();
        var transport = new RecordingHub();
        var publisher = new ConversationNotifications(db, transport);

        await publisher.PublishAsync(conversation.Id);
        Assert.Equal(new[] { a.ToString(), b.ToString() }.Order(), transport.Recipients.Order());
        Assert.DoesNotContain(outsider.ToString(), transport.Recipients);
        Assert.Equal("ConversationChanged", transport.Event);
        var payload = Assert.IsType<ConversationChanged>(Assert.Single(transport.Arguments));
        Assert.NotEqual(Guid.Empty, payload.EventId);
        Assert.Equal(conversation.Id, payload.ConversationId);
        Assert.Equal(1, payload.LatestSeq);

        // B blocks A after both connections have already been eligible.
        db.UserBlocks.Add(new UserBlock { BlockerUserId = b, BlockedUserId = a });
        await db.SaveChangesAsync();
        await publisher.PublishAsync(conversation.Id);
        Assert.Equal(new[] { b.ToString() }, transport.Recipients);

        db.UserBlocks.RemoveRange(db.UserBlocks);
        memberA.LeftAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await publisher.PublishAsync(conversation.Id);
        Assert.Equal(new[] { b.ToString() }, transport.Recipients);

        memberA.LeftAt = null;
        await db.SaveChangesAsync();
        await publisher.PublishAsync(conversation.Id);
        Assert.Equal(2, transport.Recipients.Count);
        Assert.NotEqual(payload.EventId, Assert.IsType<ConversationChanged>(transport.Arguments[0]).EventId);

        var count = transport.SendCount;
        await publisher.PublishAsync(Guid.NewGuid());
        Assert.Equal(count, transport.SendCount);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(conversation.Id));
        Assert.Equal(count, transport.SendCount);
    }

    private sealed class RecordingHub : IHubContext<ChatHub>, IHubClients, IClientProxy
    {
        IHubClients IHubContext<ChatHub>.Clients => this;
        IGroupManager IHubContext<ChatHub>.Groups => throw new NotSupportedException();
        public IReadOnlyList<string> Recipients = Array.Empty<string>();
        public string? Event;
        public object?[] Arguments = [];
        public int SendCount;
        public IClientProxy Users(IReadOnlyList<string> userIds) { Recipients = userIds; return this; }
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Event = method; Arguments = args; SendCount++; return Task.CompletedTask;
        }
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> ids) => throw new NotSupportedException();
        public IClientProxy Client(string id) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> ids) => throw new NotSupportedException();
        public IClientProxy Group(string name) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string name, IReadOnlyList<string> ids) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> names) => throw new NotSupportedException();
        public IClientProxy User(string id) => throw new NotSupportedException();
    }
}
