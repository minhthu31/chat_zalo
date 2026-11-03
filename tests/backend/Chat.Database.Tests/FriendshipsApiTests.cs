using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Chat.Application.DTOs.Friendships;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chat.Database.Tests;

public sealed class FriendshipsApiTests
{
    private static readonly Guid Caller = Guid.NewGuid();

    private sealed class FakeFriendships : IFriendshipService, IFriendshipQueryService
    {
        public string? LastOperation;
        public Guid CurrentUser;
        public Guid TargetUser;
        public FriendshipException? Error;

        private void Record(string operation, Guid current, Guid target)
        {
            LastOperation = operation; CurrentUser = current; TargetUser = target;
        }

        public Task<FriendshipDto> SendRequestAsync(Guid currentUserId, Guid targetUserId)
        {
            Record("send", currentUserId, targetUserId);
            if (Error is not null) return Task.FromException<FriendshipDto>(Error);
            return Task.FromResult(new FriendshipDto { UserId = targetUserId, Username = "target", DisplayName = "Target", Status = "Pending" });
        }

        public Task AcceptAsync(Guid currentUserId, Guid requesterUserId)
        {
            Record("accept", currentUserId, requesterUserId);
            return Error is null ? Task.CompletedTask : Task.FromException(Error);
        }

        public Task RejectAsync(Guid currentUserId, Guid requesterUserId)
        {
            Record("reject", currentUserId, requesterUserId);
            return Error is null ? Task.CompletedTask : Task.FromException(Error);
        }

        public Task RemoveAsync(Guid currentUserId, Guid targetUserId)
        {
            Record("remove", currentUserId, targetUserId);
            return Error is null ? Task.CompletedTask : Task.FromException(Error);
        }

        public Task<bool> AreFriendsAsync(Guid userAId, Guid userBId) => Task.FromResult(false);

        public Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId)
        {
            Record("friends", currentUserId, Guid.Empty);
            return Task.FromResult(new List<FriendshipDto>());
        }

        public Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId)
        {
            Record("requests", currentUserId, Guid.Empty);
            return Task.FromResult(new FriendshipRequestsDto());
        }
    }

    private static WebApplicationFactory<Program> Factory(FakeFriendships fake) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFriendshipService>();
                services.RemoveAll<IFriendshipQueryService>();
                services.AddScoped<IFriendshipService>(_ => fake);
                services.AddScoped<IFriendshipQueryService>(_ => fake);
            }));

    private static void Authenticate(WebApplicationFactory<Program> factory, HttpClient client)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, Caller.ToString()) },
            IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
            }, IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie",
            $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
    }

    [Theory]
    [InlineData("POST", "/api/friendships")]
    [InlineData("POST", "/api/friendships/00000000-0000-0000-0000-000000000001/accept")]
    [InlineData("POST", "/api/friendships/00000000-0000-0000-0000-000000000001/reject")]
    [InlineData("DELETE", "/api/friendships/00000000-0000-0000-0000-000000000001")]
    public async Task Anonymous_MutationEndpoints_Return401(string method, string path)
    {
        using var factory = Factory(new());
        using var client = factory.CreateClient();
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "POST" ? JsonContent.Create(new { targetUserId = Guid.NewGuid() }) : null
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/friendships")]
    [InlineData("POST", "/api/friendships/00000000-0000-0000-0000-000000000001/accept")]
    [InlineData("POST", "/api/friendships/00000000-0000-0000-0000-000000000001/reject")]
    [InlineData("DELETE", "/api/friendships/00000000-0000-0000-0000-000000000001")]
    public async Task Authenticated_MutationWithoutCsrf_Returns400(string method, string path)
    {
        var fake = new FakeFriendships();
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "POST" && path == "/api/friendships"
                ? JsonContent.Create(new { targetUserId = Guid.NewGuid() })
                : null
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_CSRF_TOKEN", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
        Assert.Null(fake.LastOperation);
    }

    [Fact]
    public async Task SendRequest_ValidCookieAndCsrf_UsesClaimIdentity()
    {
        var fake = new FakeFriendships();
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);
        var csrf = await client.GetFromJsonAsync<CsrfBody>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        var target = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/friendships", new { targetUserId = target });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("send", fake.LastOperation);
        Assert.Equal(Caller, fake.CurrentUser);
        Assert.Equal(target, fake.TargetUser);
        Assert.Equal("Pending", (await response.Content.ReadFromJsonAsync<FriendshipDto>())!.Status);
    }

    [Fact]
    public async Task InvalidCsrf_DoesNotInvokeService()
    {
        var fake = new FakeFriendships();
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "not-a-valid-token");

        var response = await client.PostAsJsonAsync("/api/friendships", new { targetUserId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(fake.LastOperation);
    }

    [Theory]
    [InlineData("GET", "/api/friendships", "friends")]
    [InlineData("GET", "/api/friendships/requests", "requests")]
    public async Task GetEndpoints_UseAuthenticatedPrincipal(string method, string path, string operation)
    {
        var fake = new FakeFriendships();
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(operation, fake.LastOperation);
        Assert.Equal(Caller, fake.CurrentUser);
    }

    [Theory]
    [InlineData("accept", "accept")]
    [InlineData("reject", "reject")]
    public async Task RespondEndpoints_UseRequesterIdFromRoute(string route, string operation)
    {
        var fake = new FakeFriendships();
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);
        var csrf = await client.GetFromJsonAsync<CsrfBody>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        var requester = Guid.NewGuid();

        var response = await client.PostAsync($"/api/friendships/{requester}/{route}", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(operation, fake.LastOperation);
        Assert.Equal(Caller, fake.CurrentUser);
        Assert.Equal(requester, fake.TargetUser);
    }

    [Fact]
    public async Task DeleteEndpoint_UsesAuthenticatedPrincipalAndRouteTarget()
    {
        var fake = new FakeFriendships();
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);
        var csrf = await client.GetFromJsonAsync<CsrfBody>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        var target = Guid.NewGuid();

        var response = await client.DeleteAsync($"/api/friendships/{target}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("remove", fake.LastOperation);
        Assert.Equal(Caller, fake.CurrentUser);
        Assert.Equal(target, fake.TargetUser);
    }

    private sealed record ErrorBody(string Code, string Message);
    private sealed record CsrfBody(string Token);
}
