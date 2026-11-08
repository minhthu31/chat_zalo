using System.Security.Claims;
using System.Text.Json;
using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using chat_zalo.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using chat_zalo.Hubs;

namespace Chat.Database.Tests;

public sealed class ChatHubSignalRTests
{
    private static string ConnectionString
    {
        get
        {
            var connectionString = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
                ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a dedicated migrated PostgreSQL test database.");
            var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
            if (database is null || !database.Contains("_test", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SignalR tests require a dedicated database with _test in its name.");
            }

            return connectionString;
        }
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly ChatDbContext _db;

        private TestDatabase(ChatDbContext db) => _db = db;

        public List<Guid> UserIds { get; } = [];
        public List<Guid> ConversationIds { get; } = [];

        public static async Task<TestDatabase> CreateAsync()
        {
            var db = NewContext();
            await db.Database.OpenConnectionAsync();
            var version = ((NpgsqlConnection)db.Database.GetDbConnection()).PostgreSqlVersion;
            if (version.Major != 18)
            {
                await db.DisposeAsync();
                throw new InvalidOperationException($"SignalR tests require PostgreSQL 18; found {version}.");
            }

            return new TestDatabase(db);
        }

        private static ChatDbContext NewContext() => new(
            new DbContextOptionsBuilder<ChatDbContext>()
                .UseNpgsql(ConnectionString)
                .Options);

        public async Task<Guid> AddUserAsync()
        {
            var id = Guid.NewGuid();
            var name = $"hub_{id:N}";
            _db.Users.Add(new ApplicationUser
            {
                Id = id,
                UserName = name,
                NormalizedUserName = name.ToUpperInvariant(),
                Email = $"{name}@example.test",
                NormalizedEmail = $"{name}@EXAMPLE.TEST".ToUpperInvariant(),
                DisplayName = "SignalR test",
                SecurityStamp = Guid.NewGuid().ToString()
            });
            await _db.SaveChangesAsync();
            UserIds.Add(id);
            return id;
        }

        public async Task<Guid> AddGroupConversationAsync(
            Guid ownerId,
            params (Guid UserId, DateTimeOffset? LeftAt)[] members)
        {
            var id = Guid.NewGuid();
            _db.Conversations.Add(new Chat.Domain.Entities.Conversation
            {
                Id = id,
                CreatedByUserId = ownerId,
                Kind = "Group"
            });
            _db.GroupConversations.Add(new Chat.Domain.Entities.GroupConversation
            {
                ConversationId = id,
                Name = "SignalR test group"
            });
            _db.ConversationMembers.AddRange(members.Select(member => new Chat.Domain.Entities.ConversationMember
            {
                ConversationId = id,
                UserId = member.UserId,
                Role = member.UserId == ownerId ? "Admin" : "Member",
                LeftAt = member.LeftAt
            }));
            await _db.SaveChangesAsync();
            ConversationIds.Add(id);
            return id;
        }

        public async Task<Guid> AddDirectConversationAsync(Guid firstUserId, Guid secondUserId)
        {
            var (lowId, highId) = string.Compare(
                    firstUserId.ToString("D"),
                    secondUserId.ToString("D"),
                    StringComparison.Ordinal) < 0
                ? (firstUserId, secondUserId)
                : (secondUserId, firstUserId);

            _db.Friendships.Add(new Chat.Domain.Entities.Friendship
            {
                UserLowId = lowId,
                UserHighId = highId,
                RequesterUserId = firstUserId,
                Status = "Accepted"
            });
            await _db.SaveChangesAsync();

            var conversation = await new ConversationService(
                    _db,
                    new FriendshipService(_db),
                    new BlockService(_db))
                .CreateDirectAsync(firstUserId, secondUserId);
            ConversationIds.Add(conversation.Id);
            return conversation.Id;
        }

        public async Task SetLeftAtAsync(Guid conversationId, Guid userId)
        {
            var member = await _db.ConversationMembers.SingleAsync(
                item => item.ConversationId == conversationId && item.UserId == userId);
            member.LeftAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
        }

        public Task<bool> ContainsMessageAsync(Guid messageId) =>
            _db.Messages.AnyAsync(message => message.Id == messageId);

        public async ValueTask DisposeAsync()
        {
            await _db.Friendships
                .Where(friendship => UserIds.Contains(friendship.UserLowId) || UserIds.Contains(friendship.UserHighId))
                .ExecuteDeleteAsync();
            await _db.Conversations
                .Where(conversation => ConversationIds.Contains(conversation.Id))
                .ExecuteDeleteAsync();
            await _db.Users
                .Where(user => UserIds.Contains(user.Id))
                .ExecuteDeleteAsync();
            await _db.DisposeAsync();
        }
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:ChatDb"] = ConnectionString
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ChatDbContext>>();
                services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(ConnectionString));
            });
        });

    private static HubConnection CreateConnection(WebApplicationFactory<Program> factory, Guid? userId = null) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/chat"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (userId is Guid authenticatedUserId)
                {
                    options.Headers.Add("Cookie", CreateIdentityCookie(factory, authenticatedUserId));
                }
            })
            .Build();

    private static string CreateIdentityCookie(WebApplicationFactory<Program> factory, Guid userId)
    {
        var scheme = IdentityConstants.ApplicationScheme;
        var cookieOptions = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(scheme);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            scheme);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
            },
            scheme);
        return $"{cookieOptions.Cookie.Name}={cookieOptions.TicketDataFormat.Protect(ticket)}";
    }

    private static async Task NotifyAsync(
        WebApplicationFactory<Program> factory,
        Guid conversationId,
        Guid eventId,
        long latestSeq)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IChatNotificationService>()
            .NotifyConversationChangedAsync(conversationId, eventId, latestSeq);
    }

    private static async Task<MessageResponse> SendAsync(
        WebApplicationFactory<Program> factory,
        Guid senderId,
        Guid conversationId,
        Guid clientMessageId,
        string body)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMessageService>().SendAsync(
            senderId,
            conversationId,
            new SendMessageRequest { ClientMessageId = clientMessageId, Body = body });
    }

    [Fact]
    public async Task Hub_RejectsAnonymousConnection_AndAcceptsIdentityCookie()
    {
        using var factory = CreateFactory();
        await using var anonymous = CreateConnection(factory);

        Assert.NotNull(await Record.ExceptionAsync(() => anonymous.StartAsync()));
        Assert.NotEqual(HubConnectionState.Connected, anonymous.State);

        await using var authenticated = CreateConnection(factory, Guid.NewGuid());
        await authenticated.StartAsync();
        Assert.Equal(HubConnectionState.Connected, authenticated.State);
    }

    [Fact]
    public async Task JoinConversation_AllowsActiveMember_AndRejectsOutsiderOrLeftMember()
    {
        await using var database = await TestDatabase.CreateAsync();
        var activeMember = await database.AddUserAsync();
        var leftMember = await database.AddUserAsync();
        var outsider = await database.AddUserAsync();
        var conversationId = await database.AddGroupConversationAsync(
            activeMember,
            (activeMember, null),
            (leftMember, DateTimeOffset.UtcNow.AddMinutes(-1)));

        using var factory = CreateFactory();
        await using var activeConnection = CreateConnection(factory, activeMember);
        await activeConnection.StartAsync();
        await activeConnection.InvokeAsync("JoinConversation", conversationId.ToString("D"));

        await using var outsiderConnection = CreateConnection(factory, outsider);
        await outsiderConnection.StartAsync();
        await Assert.ThrowsAsync<HubException>(() =>
            outsiderConnection.InvokeAsync("JoinConversation", conversationId.ToString("D")));

        await using var leftConnection = CreateConnection(factory, leftMember);
        await leftConnection.StartAsync();
        await Assert.ThrowsAsync<HubException>(() =>
            leftConnection.InvokeAsync("JoinConversation", conversationId.ToString("D")));
    }

    [Fact]
    public async Task LeaveConversation_RemovesOnlyOneSubscription_AndTracksMultipleConnections()
    {
        await using var database = await TestDatabase.CreateAsync();
        var userId = await database.AddUserAsync();
        var firstConversation = await database.AddGroupConversationAsync(userId, (userId, null));
        var secondConversation = await database.AddGroupConversationAsync(userId, (userId, null));

        using var factory = CreateFactory();
        await using var firstConnection = CreateConnection(factory, userId);
        await using var secondConnection = CreateConnection(factory, userId);
        await firstConnection.StartAsync();
        await secondConnection.StartAsync();
        await firstConnection.InvokeAsync("JoinConversation", firstConversation.ToString("D"));
        await firstConnection.InvokeAsync("JoinConversation", secondConversation.ToString("D"));
        await secondConnection.InvokeAsync("JoinConversation", firstConversation.ToString("D"));

        var tracker = factory.Services.GetRequiredService<IChatConnectionTracker>();
        var firstId = Assert.IsType<string>(firstConnection.ConnectionId);
        var secondId = Assert.IsType<string>(secondConnection.ConnectionId);
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(
            new[] { firstId, secondId }.OrderBy(id => id),
            tracker.GetActiveConnectionsForConversation(firstConversation, [userId]).OrderBy(id => id));

        await firstConnection.InvokeAsync("LeaveConversation", firstConversation.ToString("D"));
        Assert.Equal(
            new[] { secondId },
            tracker.GetActiveConnectionsForConversation(firstConversation, [userId]));
        Assert.Equal(
            new[] { firstId },
            tracker.GetActiveConnectionsForConversation(secondConversation, [userId]));
    }

    [Fact]
    public async Task RevokedMembership_StopsNotificationsAndPreventsRejoining()
    {
        await using var database = await TestDatabase.CreateAsync();
        var ownerId = await database.AddUserAsync();
        var userId = await database.AddUserAsync();
        var conversationId = await database.AddGroupConversationAsync(
            ownerId,
            (ownerId, null),
            (userId, null));

        using var factory = CreateFactory();
        await using var connection = CreateConnection(factory, userId);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinConversation", conversationId.ToString("D"));
        await database.SetLeftAtAsync(conversationId, userId);

        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("ConversationChanged", payload => received.TrySetResult(payload));
        await NotifyAsync(factory, conversationId, Guid.NewGuid(), 1);
        await Assert.ThrowsAsync<TimeoutException>(() => received.Task.WaitAsync(TimeSpan.FromMilliseconds(500)));
        await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync("JoinConversation", conversationId.ToString("D")));
    }

    [Fact]
    public async Task ReconnectRequiresJoinAgain_AndReceivesContractPayload()
    {
        await using var database = await TestDatabase.CreateAsync();
        var userId = await database.AddUserAsync();
        var conversationId = await database.AddGroupConversationAsync(userId, (userId, null));

        using var factory = CreateFactory();
        await using var connection = CreateConnection(factory, userId);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinConversation", conversationId.ToString("D"));
        var previousId = connection.ConnectionId;
        await connection.StopAsync();
        await connection.StartAsync();
        Assert.NotEqual(previousId, connection.ConnectionId);

        var tracker = factory.Services.GetRequiredService<IChatConnectionTracker>();
        var newId = Assert.IsType<string>(connection.ConnectionId);
        var cleanupDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < cleanupDeadline &&
               tracker.GetActiveConnectionsForConversation(conversationId, [userId]).Contains(previousId!))
        {
            await Task.Delay(50);
        }
        Assert.DoesNotContain(previousId!, tracker.GetActiveConnectionsForConversation(conversationId, [userId]));

        await connection.InvokeAsync("JoinConversation", conversationId.ToString("D"));
        Assert.Equal(new[] { newId }, tracker.GetActiveConnectionsForConversation(conversationId, [userId]));

        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("ConversationChanged", payload => received.TrySetResult(payload));
        var eventId = Guid.NewGuid();
        await NotifyAsync(factory, conversationId, eventId, 42);
        AssertPayload(await received.Task.WaitAsync(TimeSpan.FromSeconds(5)), eventId, conversationId, 42);
    }

    [Fact]
    public async Task NewMessage_PublishesAfterCommit_OnlyToJoinedActiveMembers_AndRetryDoesNotRepublish()
    {
        await using var database = await TestDatabase.CreateAsync();
        var senderId = await database.AddUserAsync();
        var peerId = await database.AddUserAsync();
        var outsiderId = await database.AddUserAsync();
        var conversationId = await database.AddDirectConversationAsync(senderId, peerId);

        using var factory = CreateFactory();
        await using var senderConnection = CreateConnection(factory, senderId);
        await using var peerConnection = CreateConnection(factory, peerId);
        await using var outsiderConnection = CreateConnection(factory, outsiderId);
        await senderConnection.StartAsync();
        await peerConnection.StartAsync();
        await outsiderConnection.StartAsync();
        await senderConnection.InvokeAsync("JoinConversation", conversationId.ToString("D"));
        await peerConnection.InvokeAsync("JoinConversation", conversationId.ToString("D"));

        var senderEvents = 0;
        var peerEvents = 0;
        var outsiderEvents = 0;
        var senderReceived = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var peerReceived = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        senderConnection.On<JsonElement>("ConversationChanged", payload =>
        {
            Interlocked.Increment(ref senderEvents);
            senderReceived.TrySetResult(payload);
        });
        peerConnection.On<JsonElement>("ConversationChanged", payload =>
        {
            Interlocked.Increment(ref peerEvents);
            peerReceived.TrySetResult(payload);
        });
        outsiderConnection.On<JsonElement>("ConversationChanged", _ => Interlocked.Increment(ref outsiderEvents));

        var clientMessageId = Guid.NewGuid();
        var created = await SendAsync(factory, senderId, conversationId, clientMessageId, "hello realtime");
        Assert.True(created.IsNewlyCreated);

        AssertPayload(await senderReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)), Guid.Empty, conversationId, created.Seq, allowAnyEventId: true);
        AssertPayload(await peerReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)), Guid.Empty, conversationId, created.Seq, allowAnyEventId: true);
        Assert.True(await database.ContainsMessageAsync(created.Id));

        var retry = await SendAsync(factory, senderId, conversationId, clientMessageId, "hello realtime");
        Assert.False(retry.IsNewlyCreated);
        Assert.Equal(created.Id, retry.Id);
        await Task.Delay(300);
        Assert.Equal(1, senderEvents);
        Assert.Equal(1, peerEvents);
        Assert.Equal(0, outsiderEvents);
    }

    private static void AssertPayload(
        JsonElement payload,
        Guid eventId,
        Guid conversationId,
        long latestSeq,
        bool allowAnyEventId = false)
    {
        Assert.Equal(
            new[] { "conversationId", "eventId", "latestSeq" },
            payload.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        if (allowAnyEventId)
        {
            Assert.NotEqual(Guid.Empty, payload.GetProperty("eventId").GetGuid());
        }
        else
        {
            Assert.Equal(eventId, payload.GetProperty("eventId").GetGuid());
        }
        Assert.Equal(conversationId, payload.GetProperty("conversationId").GetGuid());
        Assert.Equal(latestSeq, payload.GetProperty("latestSeq").GetInt64());
    }
}
