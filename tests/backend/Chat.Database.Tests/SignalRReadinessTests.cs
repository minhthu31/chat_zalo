using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Chat.Api.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;
using Chat.Api.Hubs;
using Xunit;

namespace Chat.Database.Tests;

// Acceptance probes for the contracted hub endpoint.
// No database, fake authentication scheme, or test hub replaces production routing.
public class SignalRReadinessTests
{
    private sealed class Factory : WebApplicationFactory<AuthController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment("Development");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Negotiate_WithoutValidCookie_RejectsEvenSpoofedUserId(bool spoofIdentity)
    {
        using var factory = new Factory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        if (spoofIdentity)
        {
            client.DefaultRequestHeaders.Add("X-User-Id", Guid.NewGuid().ToString());
            client.DefaultRequestHeaders.Add("Cookie", "Chat.Auth=invalid");
        }
        using var response = await client.PostAsync("/hubs/chat/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Negotiate_WithApplicationCookie_ReturnsSignalRNegotiationJson()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, Guid.NewGuid()));

        using var response = await client.PostAsync("/hubs/chat/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("negotiateVersion").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("connectionToken").GetString()));
        Assert.NotEmpty(json.GetProperty("availableTransports").EnumerateArray());
    }

    [Fact]
    public async Task WebSocket_UsesCookieIdentity_ReceivesUserEvent_RejectsArbitraryJoin()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        var sockets = factory.Server.CreateWebSocketClient();
        sockets.ConfigureRequest = request => request.Headers["Cookie"] = CreateCookie(factory, userId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await sockets.ConnectAsync(new Uri("ws://localhost/hubs/chat"), timeout.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),
            WebSocketMessageType.Text, true, timeout.Token);
        Assert.Equal("{}\u001e", await Receive(socket, timeout.Token));

        var hub = factory.Services.GetRequiredService<IHubContext<ChatHub>>();
        var payload = new ConversationChanged(Guid.NewGuid(), Guid.NewGuid(), 7);
        await hub.Clients.User(userId.ToString()).SendAsync("ConversationChanged", payload, timeout.Token);
        using var notification = JsonDocument.Parse((await Receive(socket, timeout.Token)).TrimEnd('\u001e'));
        Assert.Equal("ConversationChanged", notification.RootElement.GetProperty("target").GetString());
        var data = notification.RootElement.GetProperty("arguments")[0];
        Assert.Equal(new[] { "conversationId", "eventId", "latestSeq" },
            data.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(7, data.GetProperty("latestSeq").GetInt64());

        await socket.SendAsync(Encoding.UTF8.GetBytes(
            "{\"type\":1,\"invocationId\":\"1\",\"target\":\"JoinConversation\",\"arguments\":[\"" + Guid.NewGuid() + "\"]}\u001e"),
            WebSocketMessageType.Text, true, timeout.Token);
        using var completion = JsonDocument.Parse((await Receive(socket, timeout.Token)).TrimEnd('\u001e'));
        Assert.Equal(3, completion.RootElement.GetProperty("type").GetInt32());
        Assert.Equal("1", completion.RootElement.GetProperty("invocationId").GetString());
        Assert.True(completion.RootElement.TryGetProperty("error", out _));
    }

    [Theory]
    [InlineData("http://localhost:5000", HttpStatusCode.OK)]
    [InlineData("http://localhost", HttpStatusCode.OK)]
    [InlineData("https://untrusted.example", HttpStatusCode.Forbidden)]
    public async Task Hub_EnforcesBrowserOrigin(string origin, HttpStatusCode expected)
    {
        using var factory = new Factory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie(factory, Guid.NewGuid()));
        client.DefaultRequestHeaders.Add("Origin", origin);
        using var response = await client.PostAsync("/hubs/chat/negotiate?negotiateVersion=1", null);
        Assert.Equal(expected, response.StatusCode);
        if (origin == "http://localhost:5000")
        {
            Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        }
    }

    private static async Task<string> Receive(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string CreateCookie(Factory factory, Guid userId)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, "signalr-readiness")
        }, IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
            }, IdentityConstants.ApplicationScheme);
        return $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}";
    }
}
