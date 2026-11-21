using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Chat.Application.DTOs.Receipts;
using Chat.Application.Interfaces;
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

public sealed class MessageReceiptsApiTests
{
    private static readonly Guid CallerId = Guid.NewGuid();
    private static readonly Guid ConversationId = Guid.NewGuid();

    private sealed class FakeReceiptService : IMessageReceiptService
    {
        public Guid CurrentUserId { get; private set; }
        public Guid Conversation { get; private set; }
        public long DeliveredSeq { get; private set; }
        public long ReadSeq { get; private set; }

        public Task<ConversationReceiptsDto> GetAsync(
            Guid currentUserId,
            Guid conversationId,
            CancellationToken cancellationToken = default)
        {
            Record(currentUserId, conversationId);
            return Task.FromResult(new ConversationReceiptsDto
            {
                ConversationId = conversationId,
                Items =
                [
                    new ConversationReceiptItemDto
                    {
                        UserId = currentUserId,
                        DisplayName = "Receipt User",
                        DeliveredSeq = 12,
                        ReadSeq = 10
                    }
                ]
            });
        }

        public Task<ReceiptDto> MarkDeliveredAsync(
            Guid currentUserId,
            Guid conversationId,
            UpdateReceiptRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(currentUserId, conversationId);
            DeliveredSeq = request.Seq;
            return Task.FromResult(new ReceiptDto
            {
                UserId = currentUserId,
                DeliveredSeq = request.Seq,
                ReadSeq = ReadSeq
            });
        }

        public Task<ReceiptDto> MarkReadAsync(
            Guid currentUserId,
            Guid conversationId,
            UpdateReceiptRequest request,
            CancellationToken cancellationToken = default)
        {
            Record(currentUserId, conversationId);
            ReadSeq = request.Seq;
            DeliveredSeq = Math.Max(DeliveredSeq, request.Seq);
            return Task.FromResult(new ReceiptDto
            {
                UserId = currentUserId,
                DeliveredSeq = DeliveredSeq,
                ReadSeq = ReadSeq
            });
        }

        private void Record(Guid userId, Guid conversationId)
        {
            CurrentUserId = userId;
            Conversation = conversationId;
        }
    }

    private sealed record ErrorBody(string Code, string Message);
    private sealed record CsrfBody(string Token);

    private static WebApplicationFactory<Program> Factory(FakeReceiptService fake) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMessageReceiptService>();
                services.AddScoped<IMessageReceiptService>(_ => fake);
                services.RemoveAll<IUserStore<ApplicationUser>>();
                services.AddSingleton<IUserStore<ApplicationUser>, TestIdentityUserStore>();
            }));

    private static void Authenticate(WebApplicationFactory<Program> factory, HttpClient client)
    {
        var store = (TestIdentityUserStore)factory.Services.GetRequiredService<IUserStore<ApplicationUser>>();
        var user = store.EnsureUser(CallerId);
        var identityOptions = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var cookieOptions = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName!),
            new Claim(identityOptions.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp!)
        ], IdentityConstants.ApplicationScheme);

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
            },
            IdentityConstants.ApplicationScheme);

        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}={cookieOptions.TicketDataFormat.Protect(ticket)}");
    }

    private static async Task AddValidCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<CsrfBody>("/api/auth/csrf");
        Assert.NotNull(csrf);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("PUT", "/delivered")]
    [InlineData("PUT", "/read")]
    public async Task AllReceiptEndpoints_RequireAuthentication(string method, string suffix)
    {
        using var factory = Factory(new FakeReceiptService());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            $"/api/conversations/{ConversationId}/receipts{suffix}");

        if (method == "PUT")
            request.Content = JsonContent.Create(new UpdateReceiptRequest { Seq = 1 });

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(error);
        Assert.Equal("UNAUTHORIZED", error!.Code);
    }

    [Theory]
    [InlineData("/delivered")]
    [InlineData("/read")]
    public async Task ReceiptUpdates_RequireValidCsrf(string suffix)
    {
        using var factory = Factory(new FakeReceiptService());
        using var client = factory.CreateClient();
        Authenticate(factory, client);

        using var response = await client.PutAsJsonAsync(
            $"/api/conversations/{ConversationId}/receipts{suffix}",
            new UpdateReceiptRequest { Seq = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(error);
        Assert.Equal("INVALID_CSRF_TOKEN", error!.Code);
    }

    [Fact]
    public async Task GetDeliveredAndReadRoutes_ReturnContractDtosAndUseAuthenticatedUser()
    {
        var fake = new FakeReceiptService();
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);
        await AddValidCsrfAsync(client);

        using (var response = await client.GetAsync(
            $"/api/conversations/{ConversationId}/receipts"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<ConversationReceiptsDto>();
            Assert.NotNull(dto);
            Assert.Equal(ConversationId, dto!.ConversationId);
            var item = Assert.Single(dto.Items);
            Assert.Equal(CallerId, item.UserId);
            Assert.Equal(12L, item.DeliveredSeq);
            Assert.Equal(10L, item.ReadSeq);
        }

        using (var response = await client.PutAsJsonAsync(
            $"/api/conversations/{ConversationId}/receipts/delivered",
            new UpdateReceiptRequest { Seq = 15 }))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<ReceiptDto>();
            Assert.NotNull(dto);
            Assert.Equal(CallerId, dto!.UserId);
            Assert.Equal(15L, dto.DeliveredSeq);
        }

        using (var response = await client.PutAsJsonAsync(
            $"/api/conversations/{ConversationId}/receipts/read",
            new UpdateReceiptRequest { Seq = 18 }))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<ReceiptDto>();
            Assert.NotNull(dto);
            Assert.Equal(18L, dto!.ReadSeq);
            Assert.Equal(18L, dto.DeliveredSeq);
        }

        Assert.Equal(CallerId, fake.CurrentUserId);
        Assert.Equal(ConversationId, fake.Conversation);
    }
}
