using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chat.Api.Controllers;
using Chat.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Chat.Database.Tests;

// Real Kestrel HTTP, Identity cookies, antiforgery, application services and PostgreSQL.
public class ConversationsHttpPostgresTests
{
    private sealed class Factory : WebApplicationFactory<ConversationsController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var cs = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
                ?? throw new InvalidOperationException("Set CHAT_TEST_CONNECTION_STRING to a migrated test database.");
            var database = new NpgsqlConnectionStringBuilder(cs).Database ?? "";
            if (!database.Contains("_test", StringComparison.OrdinalIgnoreCase)
                && !database.Contains("_review", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A dedicated test/review database is required.");
            builder.UseEnvironment("Development")
                .UseSetting("ConnectionStrings:DefaultConnection", cs)
                .UseSetting("ConnectionStrings:ChatDb", cs);
        }
    }

    [Fact]
    public async Task CookieCsrfFriendshipsBlocksAndConcurrentCreation_FollowContract()
    {
        using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var a = new Session(factory); using var b = new Session(factory); using var outsider = new Session(factory);
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        anonymous.BaseAddress = a.BaseAddress;
        anonymous.DefaultRequestHeaders.Add("X-User-Id", Guid.NewGuid().ToString());
        var anonymousResponse = await anonymous.GetAsync("/api/conversations");
        Assert.True(anonymousResponse.StatusCode == HttpStatusCode.Unauthorized,
            $"{anonymous.BaseAddress}: {(int)anonymousResponse.StatusCode} {await anonymousResponse.Content.ReadAsStringAsync()}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/conversations/direct", new { targetUserId = Guid.NewGuid() })).StatusCode);
        var aid = await a.Register(); var bid = await b.Register(); await outsider.Register();
        Assert.Equal(HttpStatusCode.OK, (await a.Send(HttpMethod.Get, "/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Send(HttpMethod.Get, "/api/auth/me")).StatusCode);

        var missing = await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = bid }, csrf: false);
        await Error(missing, HttpStatusCode.BadRequest, "INVALID_CSRF_TOKEN");
        await Error(await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = bid }, invalidCsrf: true), HttpStatusCode.BadRequest, "INVALID_CSRF_TOKEN");
        await Error(await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = Guid.NewGuid() }), HttpStatusCode.NotFound, "NOT_FOUND");
        await Error(await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = Guid.Empty }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = "invalid" }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = aid }), HttpStatusCode.Forbidden, "DIRECT_CONVERSATION_NOT_ALLOWED");
        await Error(await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = bid }), HttpStatusCode.Forbidden, "DIRECT_CONVERSATION_NOT_ALLOWED");

        Assert.Equal(HttpStatusCode.Created, (await a.Send(HttpMethod.Post, "/api/friendships", new { targetUserId = bid })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Send(HttpMethod.Post, $"/api/friendships/{aid}/accept")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Send(HttpMethod.Put, $"/api/blocks/{aid}")).StatusCode);
        await Error(await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = bid }), HttpStatusCode.Forbidden, "DIRECT_CONVERSATION_NOT_ALLOWED");
        await Error(await b.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = aid }), HttpStatusCode.Forbidden, "DIRECT_CONVERSATION_NOT_ALLOWED");
        Assert.Equal(HttpStatusCode.NoContent, (await b.Send(HttpMethod.Delete, $"/api/blocks/{aid}")).StatusCode);

        var responses = await Task.WhenAll(
            a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = bid }),
            b.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = aid }));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var dto = await Json(responses[0]); var id = dto.GetProperty("id").GetGuid();
        Assert.Equal(id, (await Json(responses[1])).GetProperty("id").GetGuid());
        Assert.Equal(new[] { "id", "kind", "lastMessageSeq" }, dto.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal("Direct", dto.GetProperty("kind").GetString());
        Assert.Equal(0, dto.GetProperty("lastMessageSeq").GetInt64());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            Assert.Equal(1, await db.DirectConversations.CountAsync(d => (d.UserLowId == aid && d.UserHighId == bid) || (d.UserLowId == bid && d.UserHighId == aid)));
            var members = await db.ConversationMembers.Where(m => m.ConversationId == id).ToListAsync();
            Assert.Equal(2, members.Count);
            Assert.All(members, m => Assert.Null(m.LeftAt));
        }
        Assert.DoesNotContain((await Json(await outsider.Send(HttpMethod.Get, "/api/conversations"))).EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);

        Assert.Equal(HttpStatusCode.NoContent, (await a.Send(HttpMethod.Delete, $"/api/friendships/{bid}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = bid })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Send(HttpMethod.Put, $"/api/blocks/{aid}")).StatusCode);
        await Error(await a.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = bid }), HttpStatusCode.Forbidden, "CONVERSATION_ACCESS_DENIED");
        Assert.Empty((await Json(await a.Send(HttpMethod.Get, "/api/conversations"))).EnumerateArray());
        Assert.Contains((await Json(await b.Send(HttpMethod.Get, "/api/conversations"))).EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.OK, (await b.Send(HttpMethod.Post, "/api/conversations/direct", new { targetUserId = aid })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Send(HttpMethod.Delete, $"/api/blocks/{aid}")).StatusCode);
        Assert.Contains((await Json(await a.Send(HttpMethod.Get, "/api/conversations"))).EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Send(HttpMethod.Post, "/api/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.Send(HttpMethod.Get, "/api/conversations")).StatusCode);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static async Task Error(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        Assert.Equal(status, r.StatusCode);
        Assert.Equal(code, (await Json(r)).GetProperty("code").GetString());
    }

    private sealed class Session : IDisposable
    {
        private readonly HttpClient client;
        public Uri BaseAddress => client.BaseAddress!;
        public Session(Factory factory)
        {
            client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var addresses = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            // Development host filtering permits localhost; retain the allocated port.
            client.BaseAddress = new UriBuilder(addresses.Addresses.Single()) { Host = "localhost" }.Uri;
        }
        public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, bool csrf = true, bool invalidCsrf = false)
        {
            var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            if (method != HttpMethod.Get && csrf)
            {
                var token = (await Json(await client.GetAsync("/api/auth/csrf"))).GetProperty("token").GetString();
                request.Headers.Add("X-CSRF-TOKEN", invalidCsrf ? "invalid" : token);
            }
            return await client.SendAsync(request);
        }
        public async Task<Guid> Register()
        {
            var name = "conv" + Guid.NewGuid().ToString("N");
            var r = await Send(HttpMethod.Post, "/api/auth/register", new { username = name, email = name + "@example.com", password = "Review123!" });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var id = (await Json(r)).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/auth/login", new { usernameOrEmail = name, password = "Review123!" })).StatusCode);
            return id;
        }
        public void Dispose() => client.Dispose();
    }
}
