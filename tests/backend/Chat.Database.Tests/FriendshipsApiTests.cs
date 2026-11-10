using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Chat.Application.DTOs.Friendships;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Infrastructure.Identity;
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
    private static readonly Guid Target = Guid.NewGuid();

    private sealed class FakeFriendships : IFriendshipService
    {
        public readonly List<string> Calls = [];
        public Guid CurrentUser { get; private set; }
        public Guid TargetUser { get; private set; }
        public FriendshipException? SendError { get; set; }
        public FriendshipException? AcceptError { get; set; }
        public FriendshipException? RejectError { get; set; }
        public FriendshipException? RemoveError { get; set; }

        public Task<FriendshipDto> SendRequestAsync(Guid currentUserId, Guid targetUserId)
        {
            Record("Send", currentUserId, targetUserId);
            var error = SendError;
            return error is null
                ? Task.FromResult(new FriendshipDto
                {
                    UserId = targetUserId,
                    Username = "target-user",
                    DisplayName = "Target User",
                    Status = "Pending"
                })
                : Task.FromException<FriendshipDto>(error);
        }

        public Task AcceptAsync(Guid currentUserId, Guid requesterUserId)
        {
            Record("Accept", currentUserId, requesterUserId);
            var error = AcceptError;
            return error is null ? Task.CompletedTask : Task.FromException(error);
        }

        public Task RejectAsync(Guid currentUserId, Guid requesterUserId)
        {
            Record("Reject", currentUserId, requesterUserId);
            var error = RejectError;
            return error is null ? Task.CompletedTask : Task.FromException(error);
        }

        public Task RemoveAsync(Guid currentUserId, Guid targetUserId)
        {
            Record("Remove", currentUserId, targetUserId);
            var error = RemoveError;
            return error is null ? Task.CompletedTask : Task.FromException(error);
        }

        public Task<bool> AreFriendsAsync(Guid userAId, Guid userBId) => Task.FromResult(false);

        public Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId)
        {
            Record("GetFriends", currentUserId, Guid.Empty);
            return Task.FromResult(new List<FriendshipDto>
            {
                new()
                {
                    UserId = Target,
                    Username = "target-user",
                    DisplayName = "Target User",
                    Status = "Accepted"
                }
            });
        }

        public Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId)
        {
            Record("GetRequests", currentUserId, Guid.Empty);
            return Task.FromResult(new FriendshipRequestsDto
            {
                Incoming =
                [
                    new FriendshipDto
                    {
                        UserId = Target,
                        Username = "target-user",
                        DisplayName = "Target User",
                        Status = "Pending"
                    }
                ],
                Outgoing = []
            });
        }

        private void Record(string action, Guid currentUser, Guid targetUser)
        {
            Calls.Add(action);
            CurrentUser = currentUser;
            TargetUser = targetUser;
        }
    }

    private static WebApplicationFactory<Program> Factory(FakeFriendships fake) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFriendshipService>();
                services.AddScoped<IFriendshipService>(_ => fake);
                services.RemoveAll<IUserStore<ApplicationUser>>();
                services.AddSingleton<IUserStore<ApplicationUser>, TestIdentityUserStore>();
            }));

    private static void Authenticate(WebApplicationFactory<Program> factory, HttpClient client)
    {
        // Issue a protected Identity cookie with a real test user and matching Security Stamp.
        var store = (TestIdentityUserStore)factory.Services.GetRequiredService<IUserStore<ApplicationUser>>();
        var user = store.EnsureUser(Caller);
        var identityOptions = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName!),
                new Claim(identityOptions.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp!)
            ],
            IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        }, IdentityConstants.ApplicationScheme);

        client.DefaultRequestHeaders.Add("Cookie",
            $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
    }

    private static async Task AddValidCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<CsrfBody>("/api/auth/csrf");
        Assert.NotNull(csrf);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
    }

    [Theory]
    [InlineData("GET", "/api/friendships")]
    [InlineData("GET", "/api/friendships/requests")]
    [InlineData("POST", "/api/friendships")]
    [InlineData("POST", "/api/friendships/00000000-0000-0000-0000-000000000001/accept")]
    [InlineData("POST", "/api/friendships/00000000-0000-0000-0000-000000000001/reject")]
    [InlineData("DELETE", "/api/friendships/00000000-0000-0000-0000-000000000001")]
    public async Task AllSixEndpoints_RequireAuthentication(string method, string path)
    {
        using var factory = Factory(new());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST" && path == "/api/friendships")
            request.Content = JsonContent.Create(new { targetUserId = Target });

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(error);
        Assert.Equal("UNAUTHORIZED", error!.Code);
    }

    [Theory]
    [InlineData("POST", "/api/friendships")]
    [InlineData("POST", "/api/friendships/00000000-0000-0000-0000-000000000001/accept")]
    [InlineData("POST", "/api/friendships/00000000-0000-0000-0000-000000000001/reject")]
    [InlineData("DELETE", "/api/friendships/00000000-0000-0000-0000-000000000001")]
    public async Task AuthenticatedUnsafeEndpoints_RejectMissingCsrf(string method, string path)
    {
        var fake = new FakeFriendships();
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST" && path == "/api/friendships")
            request.Content = JsonContent.Create(new { targetUserId = Target });

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(error);
        Assert.Equal("INVALID_CSRF_TOKEN", error!.Code);
        Assert.Empty(fake.Calls);
    }

    [Theory]
    [InlineData("POST", "/api/friendships")]
    [InlineData("POST", "/api/friendships/00000000-0000-0000-0000-000000000001/accept")]
    [InlineData("POST", "/api/friendships/00000000-0000-0000-0000-000000000001/reject")]
    [InlineData("DELETE", "/api/friendships/00000000-0000-0000-0000-000000000001")]
    public async Task AuthenticatedUnsafeEndpoints_RejectInvalidCsrf(string method, string path)
    {
        var fake = new FakeFriendships();
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "not-a-valid-antiforgery-token");

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST" && path == "/api/friendships")
            request.Content = JsonContent.Create(new { targetUserId = Target });

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(error);
        Assert.Equal("INVALID_CSRF_TOKEN", error!.Code);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task ValidCsrf_ExercisesAllSixEndpointsAndTheirHttpContracts()
    {
        var fake = new FakeFriendships();
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);
        await AddValidCsrfAsync(client);

        // POST /api/friendships -> 201 + FriendshipDto
        using (var response = await client.PostAsJsonAsync("/api/friendships", new { targetUserId = Target }))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<FriendshipDto>();
            Assert.NotNull(dto);
            Assert.Equal(Target, dto!.UserId);
            Assert.Equal("target-user", dto!.Username);
            Assert.Equal("Target User", dto!.DisplayName);
            Assert.Equal("Pending", dto!.Status);
        }
        Assert.Equal(Caller, fake.CurrentUser);
        Assert.Equal(Target, fake.TargetUser);

        // GET /api/friendships -> 200 + FriendshipDto[]
        using (var response = await client.GetAsync("/api/friendships"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var friends = await response.Content.ReadFromJsonAsync<List<FriendshipDto>>();
            Assert.NotNull(friends);
            var friend = Assert.Single(friends!);
            Assert.Equal(Target, friend.UserId);
            Assert.Equal("Accepted", friend.Status);
        }

        // GET /api/friendships/requests -> 200 + incoming/outgoing DTO lists
        using (var response = await client.GetAsync("/api/friendships/requests"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var requests = await response.Content.ReadFromJsonAsync<FriendshipRequestsDto>();
            Assert.NotNull(requests);
            Assert.Equal(Target, Assert.Single(requests!.Incoming).UserId);
            Assert.Empty(requests!.Outgoing);
        }

        // POST accept -> 204. The service receives the authenticated caller, not body data.
        using (var response = await client.PostAsync($"/api/friendships/{Target}/accept", content: null))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // POST reject -> 204
        using (var response = await client.PostAsync($"/api/friendships/{Target}/reject", content: null))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // DELETE -> 204
        using (var response = await client.DeleteAsync($"/api/friendships/{Target}"))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(Caller, fake.CurrentUser);
        Assert.Equal(Target, fake.TargetUser);
        Assert.Equal(
            new[] { "Send", "GetFriends", "GetRequests", "Accept", "Reject", "Remove" },
            fake.Calls);
    }

    [Fact]
    public async Task ServiceErrors_AreReturnedAsStableJsonAndEnforceRecipientPermission()
    {
        var fake = new FakeFriendships
        {
            SendError = new FriendshipException(409, "FRIEND_REQUEST_ALREADY_EXISTS", "Request already exists."),
            AcceptError = new FriendshipException(403, "FRIEND_REQUEST_RECIPIENT_ONLY", "Only recipient may accept."),
            RejectError = new FriendshipException(403, "FRIEND_REQUEST_RECIPIENT_ONLY", "Only recipient may reject."),
            RemoveError = new FriendshipException(403, "FRIEND_REQUEST_SENDER_ONLY", "Only sender may cancel.")
        };
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);
        await AddValidCsrfAsync(client);

        using (var response = await client.PostAsJsonAsync("/api/friendships", new { targetUserId = Target }))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
            Assert.NotNull(error);
            Assert.Equal("FRIEND_REQUEST_ALREADY_EXISTS", error!.Code);
            Assert.Equal("Request already exists.", error!.Message);
        }

        using (var response = await client.PostAsync($"/api/friendships/{Target}/accept", content: null))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
            Assert.NotNull(error);
            Assert.Equal("FRIEND_REQUEST_RECIPIENT_ONLY", error!.Code);
            Assert.Equal("Only recipient may accept.", error!.Message);
        }

        using (var response = await client.PostAsync($"/api/friendships/{Target}/reject", content: null))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
            Assert.NotNull(error);
            Assert.Equal("FRIEND_REQUEST_RECIPIENT_ONLY", error!.Code);
            Assert.Equal("Only recipient may reject.", error!.Message);
        }

        using (var response = await client.DeleteAsync($"/api/friendships/{Target}"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
            Assert.NotNull(error);
            Assert.Equal("FRIEND_REQUEST_SENDER_ONLY", error!.Code);
            Assert.Equal("Only sender may cancel.", error!.Message);
        }
    }

    private sealed record ErrorBody(string Code, string Message);
    private sealed record CsrfBody(string Token);
}
