namespace Chat.Database.Tests;

using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;
using Xunit;

public class DatabaseConstraintsTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a dedicated migrated test database.");

    private ChatDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ChatDbContext(options);
    }

    [Fact]
    public async Task ApplicationUser_CanBeCreated_AndEnforcesUniqueNormalizedUserName()
    {
        using var context = CreateContext();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var user1 = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"user_{uniqueSuffix}",
            NormalizedUserName = $"USER_{uniqueSuffix}",
            Email = $"user_{uniqueSuffix}@example.com",
            NormalizedEmail = $"USER_{uniqueSuffix}@EXAMPLE.COM",
            DisplayName = $"User {uniqueSuffix}"
        };

        context.Users.Add(user1);
        await context.SaveChangesAsync();

        var userDuplicate = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"user_{uniqueSuffix}",
            NormalizedUserName = $"USER_{uniqueSuffix}", // Duplicate
            Email = $"different_{uniqueSuffix}@example.com",
            NormalizedEmail = $"DIFFERENT_{uniqueSuffix}@EXAMPLE.COM",
            DisplayName = "Duplicate"
        };

        context.Users.Add(userDuplicate);
        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Fact]
    public async Task DirectConversation_EnforcesSingleConversationPerUserPair()
    {
        using var context = CreateContext();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var lowId = id1.CompareTo(id2) < 0 ? id1 : id2;
        var highId = id1.CompareTo(id2) < 0 ? id2 : id1;

        var userLow = new ApplicationUser
        {
            Id = lowId,
            UserName = $"u_{lowId:N}"[..15],
            NormalizedUserName = $"U_{lowId:N}"[..15],
            DisplayName = "User Low"
        };
        var userHigh = new ApplicationUser
        {
            Id = highId,
            UserName = $"u_{highId:N}"[..15],
            NormalizedUserName = $"U_{highId:N}"[..15],
            DisplayName = "User High"
        };
        context.Users.AddRange(userLow, userHigh);

        var conv1 = new Conversation
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = lowId,
            Kind = "Direct"
        };
        var direct1 = new DirectConversation
        {
            ConversationId = conv1.Id,
            UserLowId = lowId,
            UserHighId = highId
        };
        context.Conversations.Add(conv1);
        context.DirectConversations.Add(direct1);
        await context.SaveChangesAsync();

        // Second direct conversation between the same pair must fail
        var conv2 = new Conversation
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = highId,
            Kind = "Direct"
        };
        var direct2 = new DirectConversation
        {
            ConversationId = conv2.Id,
            UserLowId = lowId,
            UserHighId = highId
        };
        context.Conversations.Add(conv2);
        context.DirectConversations.Add(direct2);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Fact]
    public async Task Messages_EnforcesUniqueSeq_AndIdempotencyByClientMessageId()
    {
        using var context = CreateContext();
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"u_{userId:N}"[..15],
            NormalizedUserName = $"U_{userId:N}"[..15],
            DisplayName = "Sender"
        };
        var conv = new Conversation
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = userId,
            Kind = "Direct"
        };
        context.Users.Add(user);
        context.Conversations.Add(conv);

        var clientMsgId = Guid.NewGuid();
        var msg1 = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conv.Id,
            SenderUserId = userId,
            ClientMessageId = clientMsgId,
            Seq = 1,
            Body = "Hello world"
        };
        context.Messages.Add(msg1);
        await context.SaveChangesAsync();

        // Duplicate Seq in same conversation must fail
        var msgDuplicateSeq = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conv.Id,
            SenderUserId = userId,
            ClientMessageId = Guid.NewGuid(),
            Seq = 1, // Duplicate Seq
            Body = "Another message with same Seq"
        };
        context.Messages.Add(msgDuplicateSeq);
        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());

        using var context2 = CreateContext();
        // Duplicate ClientMessageId for same sender and conversation must fail
        var msgDuplicateClientMsgId = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conv.Id,
            SenderUserId = userId,
            ClientMessageId = clientMsgId, // Duplicate ClientMessageId
            Seq = 2,
            Body = "Hello retry"
        };
        context2.Messages.Add(msgDuplicateClientMsgId);
        await Assert.ThrowsAsync<DbUpdateException>(async () => await context2.SaveChangesAsync());
    }

    [Fact]
    public async Task AtomicSeqGeneration_UsingRowLockTransaction_CoordinatesCorrectly()
    {
        using var context = CreateContext();
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"u_{userId:N}"[..15],
            NormalizedUserName = $"U_{userId:N}"[..15],
            DisplayName = "Atomic Test User"
        };
        var conv = new Conversation
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = userId,
            Kind = "Direct",
            LastMessageSeq = 0
        };
        context.Users.Add(user);
        context.Conversations.Add(conv);
        await context.SaveChangesAsync();

        // Simulate S1-07 message send with transaction & row lock
        await Task.WhenAll(Enumerable.Range(1, 3).Select(async i =>
        {
            using var worker = CreateContext();
            using var tx = await worker.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            // Row-level lock on the conversation
            var lockedConv = await worker.Conversations
                .FromSqlInterpolated($"SELECT * FROM \"Conversations\" WHERE \"Id\" = {conv.Id} FOR UPDATE")
                .SingleAsync();

            lockedConv.LastMessageSeq += 1;
            lockedConv.LastActivityAt = DateTimeOffset.UtcNow;

            var newMsg = new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conv.Id,
                SenderUserId = userId,
                ClientMessageId = Guid.NewGuid(),
                Seq = lockedConv.LastMessageSeq,
                Body = $"Message #{i}",
                CreatedAt = DateTimeOffset.UtcNow
            };
            worker.Messages.Add(newMsg);

            await worker.SaveChangesAsync();
            await tx.CommitAsync();
        }));

        // Verify messages in DB
        using var verifyContext = CreateContext();
        var messages = await verifyContext.Messages
            .Where(m => m.ConversationId == conv.Id)
            .OrderBy(m => m.Seq)
            .ToListAsync();

        Assert.Equal(3, messages.Count);
        Assert.Equal(1, messages[0].Seq);
        Assert.Equal(2, messages[1].Seq);
        Assert.Equal(3, messages[2].Seq);

        var finalConv = await verifyContext.Conversations.FindAsync(conv.Id);
        Assert.NotNull(finalConv);
        Assert.Equal(3, finalConv.LastMessageSeq);
    }

    [Fact]
    public async Task CheckConstraint_SelfBlock_ThrowsException()
    {
        using var context = CreateContext();
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"u_{userId:N}"[..15],
            NormalizedUserName = $"U_{userId:N}"[..15],
            DisplayName = "Blocker"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var selfBlock = new UserBlock
        {
            BlockerUserId = userId,
            BlockedUserId = userId // Self block violated
        };
        context.UserBlocks.Add(selfBlock);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Fact]
    public async Task CheckConstraint_FriendshipOrder_ThrowsExceptionWhenLowGreater()
    {
        using var context = CreateContext();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var lowId = id1.CompareTo(id2) < 0 ? id1 : id2;
        var highId = id1.CompareTo(id2) < 0 ? id2 : id1;

        var u1 = new ApplicationUser { Id = lowId, UserName = $"u_{lowId:N}"[..15], NormalizedUserName = $"U_{lowId:N}"[..15], DisplayName = "U1" };
        var u2 = new ApplicationUser { Id = highId, UserName = $"u_{highId:N}"[..15], NormalizedUserName = $"U_{highId:N}"[..15], DisplayName = "U2" };
        context.Users.AddRange(u1, u2);
        await context.SaveChangesAsync();

        // Inverted: LowId > HighId
        var invalidFriendship = new Friendship
        {
            UserLowId = highId,
            UserHighId = lowId,
            RequesterUserId = highId,
            Status = "Pending"
        };
        context.Friendships.Add(invalidFriendship);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }
}
