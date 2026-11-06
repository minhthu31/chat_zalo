using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using chat_zalo.Services;
using Chat.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

// Real Kestrel + Identity login/CSRF + PostgreSQL + SignalR WebSockets.
public sealed record ConversationChanged(Guid EventId, Guid ConversationId, long LatestSeq);

public sealed class SignalRPostgresTests
{
    // Fail AFTER INSERT/UPDATE, but before MessageService commits: prove rollback
    // removes both the message and its allocated seq, and emits no notification.
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Chat.Domain.Entities.Message>()
                .Any(e => e.Entity.Body == "__task9_rollback__"))
                throw new InvalidOperationException("Injected task9 pre-commit failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var cs = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
                ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a migrated dedicated test database.");
            if (!(new NpgsqlConnectionStringBuilder(cs).Database ?? "").Contains("_test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use a dedicated _test database.");
            builder.UseEnvironment("Development").UseSetting("ConnectionStrings:ChatDb", cs)
                .UseSetting("ConnectionStrings:DefaultConnection", cs);
            builder.ConfigureServices(services => services.AddDbContext<ChatDbContext>(options =>
                options.AddInterceptors(new FailAfterSave())));
        }
    }

    [Fact]
    public async Task HttpCommit_NotifiesOnlyAuthorizedUsers_RetryReconnectBlockAndLeave()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var bootstrap = factory.CreateClient();
        var addresses = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        var url = new UriBuilder(addresses.Addresses.Single()) { Host = "localhost" }.Uri;
        await using var a = new Session(url); await using var b = new Session(url); await using var x = new Session(url);
        var aid = await a.LoginNew(); var bid = await b.LoginNew(); await x.LoginNew();
        Assert.Equal(HttpStatusCode.Created, (await a.Send(HttpMethod.Post, "api/friendships", new { targetUserId = bid })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Send(HttpMethod.Post, $"api/friendships/{aid}/accept")).StatusCode);
        var created = await a.Send(HttpMethod.Post, "api/conversations/direct", new { targetUserId = bid });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var cid = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        var path = $"api/conversations/{cid}/messages";
        await a.Connect(); await b.Connect(); await x.Connect();
        await a.Hub!.InvokeAsync("JoinConversation", cid.ToString());
        await b.Hub!.InvokeAsync("JoinConversation", cid.ToString());
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => x.Hub!.InvokeAsync("JoinConversation", cid.ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await x.Send(HttpMethod.Get, path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await x.Send(HttpMethod.Post, path, new { clientMessageId = Guid.NewGuid(), body = "intruder" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Send(HttpMethod.Post, path,
            new { clientMessageId = Guid.NewGuid(), body = "no csrf" }, false)).StatusCode);

        Assert.Equal(HttpStatusCode.InternalServerError, (await a.Send(HttpMethod.Post, path,
            new { clientMessageId = Guid.NewGuid(), body = "__task9_rollback__" })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            Assert.False(await db.Messages.AnyAsync(m => m.ConversationId == cid));
            Assert.Equal(0, (await db.Conversations.SingleAsync(c => c.Id == cid)).LastMessageSeq);
        }
        await None(a, b, x);

        var request = new { clientMessageId = Guid.NewGuid(), body = "PostgreSQL â†’ SignalR" };
        var sent = await a.Send(HttpMethod.Post, path, request);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        var message = await sent.Content.ReadFromJsonAsync<Chat.Application.DTOs.Messages.MessageResponse>();
        var ea = await a.Next(); var eb = await b.Next();
        Assert.Equal(cid, ea.ConversationId); Assert.Equal(1, ea.LatestSeq); Assert.Equal(ea, eb);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            Assert.Equal(message!.Id, (await db.Messages.SingleAsync(m => m.ConversationId == cid)).Id);
            Assert.Equal(ea.LatestSeq, (await db.Conversations.SingleAsync(c => c.Id == cid)).LastMessageSeq);
        }
        Assert.Equal(HttpStatusCode.OK, (await a.Send(HttpMethod.Post, path, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.Send(HttpMethod.Post, path,
            new { request.clientMessageId, body = "changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Send(HttpMethod.Get, path + "?beforeSeq=2&afterSeq=0")).StatusCode);
        await None(a, b, x);

        // Concurrent identical retry allocates one seq and emits one event.
        var retry = new { clientMessageId = Guid.NewGuid(), body = "concurrent retry" };
        var concurrent = await Task.WhenAll(a.Send(HttpMethod.Post, path, retry), a.Send(HttpMethod.Post, path, retry));
        Assert.Single(concurrent, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(concurrent, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(2, (await a.Next()).LatestSeq); Assert.Equal(2, (await b.Next()).LatestSeq);
        await None(a, b, x);

        await b.Hub!.StopAsync();
        Assert.Equal(HttpStatusCode.Created, (await a.Send(HttpMethod.Post, path,
            new { clientMessageId = Guid.NewGuid(), body = "missed while offline" })).StatusCode);
        Assert.Equal(3, (await a.Next()).LatestSeq);
        await b.Hub.StartAsync();
        await b.Hub.InvokeAsync("JoinConversation", cid.ToString());
        var missed = await (await b.Send(HttpMethod.Get, path + "?afterSeq=2")).Content
            .ReadFromJsonAsync<List<Chat.Application.DTOs.Messages.MessageResponse>>();
        Assert.Equal(3, Assert.Single(missed!).Seq);

        Assert.Equal(HttpStatusCode.NoContent, (await b.Send(HttpMethod.Put, $"api/blocks/{aid}")).StatusCode);
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() =>
            a.Hub!.InvokeAsync("JoinConversation", cid.ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Send(HttpMethod.Get, path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Send(HttpMethod.Get, path)).StatusCode);
        foreach (var session in new[] { a, b })
            Assert.Equal(HttpStatusCode.Forbidden, (await session.Send(HttpMethod.Post, path,
                new { clientMessageId = Guid.NewGuid(), body = "blocked" })).StatusCode);
        await None(a, b, x);
        await Publish(factory, cid);
        Assert.Equal(3, (await b.Next()).LatestSeq);
        await None(a, x);

        Assert.Equal(HttpStatusCode.NoContent, (await b.Send(HttpMethod.Delete, $"api/blocks/{aid}")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            await db.ConversationMembers.Where(m => m.ConversationId == cid && m.UserId == aid)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.LeftAt, DateTimeOffset.UtcNow));
        }
        await Publish(factory, cid);
        Assert.Equal(3, (await b.Next()).LatestSeq);
        await None(a, x);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Send(HttpMethod.Get, path)).StatusCode);
    }

    private static async Task Publish(Factory factory, Guid cid)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        var seq = await db.Conversations.Where(c => c.Id == cid).Select(c => c.LastMessageSeq).SingleAsync();
        await scope.ServiceProvider.GetRequiredService<IChatNotificationService>().NotifyConversationChangedAsync(cid, Guid.NewGuid(), seq);
    }

    private static async Task None(params Session[] sessions)
    {
        await Task.Delay(250);
        foreach (var session in sessions) Assert.False(session.Events.Reader.TryRead(out _), "Unexpected notification.");
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly CookieContainer cookies = new();
        private readonly HttpClient http;
        public HubConnection? Hub;
        public readonly Channel<ConversationChanged> Events = Channel.CreateUnbounded<ConversationChanged>();
        public Session(Uri url) => http = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = url };
        public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, bool csrf = true)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            if (method != HttpMethod.Get && csrf)
            {
                var token = await http.GetFromJsonAsync<System.Text.Json.JsonElement>("api/auth/csrf");
                request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
            }
            return await http.SendAsync(request);
        }
        public async Task<Guid> LoginNew()
        {
            var username = "s9" + Guid.NewGuid().ToString("N");
            var register = await Send(HttpMethod.Post, "api/auth/register", new { username, email = username + "@example.com", password = "Task9Test123!" });
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
            var user = await register.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "api/auth/login", new { usernameOrEmail = username, password = "Task9Test123!" })).StatusCode);
            return user.GetProperty("id").GetGuid();
        }
        public async Task Connect()
        {
            Hub = new HubConnectionBuilder().WithUrl(new Uri(http.BaseAddress!, "hubs/chat"), options =>
            {
                options.Cookies = cookies;
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
            }).Build();
            Hub.On<ConversationChanged>("ConversationChanged", change => Events.Writer.TryWrite(change));
            await Hub.StartAsync();
        }
        public async Task<ConversationChanged> Next()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await Events.Reader.ReadAsync(timeout.Token);
        }
        public async ValueTask DisposeAsync()
        {
            if (Hub is not null) await Hub.DisposeAsync();
            http.Dispose();
        }
    }
}
