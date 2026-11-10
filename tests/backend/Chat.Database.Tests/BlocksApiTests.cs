using Chat.Infrastructure.Services;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chat.Database.Tests;

public class BlocksApiTests
{
    private static readonly Guid Caller = Guid.NewGuid();

    private sealed class FakeBlocks : IBlockService
    {
        public int Calls;
        public Guid CurrentUser;
        public Guid TargetUser;
        public BlockException? Error;
        public Task BlockAsync(Guid a, Guid b)
        {
            Calls++; CurrentUser = a; TargetUser = b;
            return Error is null ? Task.CompletedTask : Task.FromException(Error);
        }
        public Task UnblockAsync(Guid a, Guid b) => BlockAsync(a, b);
        public Task<bool> IsBlockedAsync(Guid a, Guid b) => Task.FromResult(false);
        public Task<bool> HasBlockedAsync(Guid a, Guid b) => Task.FromResult(false);
    }

    private static WebApplicationFactory<Program> Factory(FakeBlocks fake) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBlockService>();
            services.AddScoped<IBlockService>(_ => fake);
        }));

    private static void Authenticate(WebApplicationFactory<Program> factory, HttpClient client)
    {
        // Use the real CHAT-2 cookie scheme and ticket protection, without a DB login.
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Caller.ToString()) }, IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        }, IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Anonymous_Is401Json(string method)
    {
        using var factory = Factory(new());
        using var client = factory.CreateClient();
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"/api/blocks/{Guid.NewGuid()}"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Theory]
    [InlineData("PUT", false)]
    [InlineData("DELETE", false)]
    [InlineData("PUT", true)]
    [InlineData("DELETE", true)]
    public async Task Authenticated_InvalidCsrf_Is400(string method, bool invalid)
    {
        var fake = new FakeBlocks();
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);
        if (invalid) client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "invalid");
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"/api/blocks/{Guid.NewGuid()}"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_CSRF_TOKEN", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
        Assert.Equal(0, fake.Calls);
    }

    [Theory]
    [InlineData("PUT", 204)]
    [InlineData("DELETE", 204)]
    [InlineData("PUT", 400)]
    [InlineData("PUT", 404)]
    [InlineData("DELETE", 404)]
    public async Task ValidCsrf_UsesPrincipalAndContract(string method, int status)
    {
        var fake = new FakeBlocks { Error = status == 204 ? null : new BlockException(status, status == 400 ? "SELF_BLOCK_NOT_ALLOWED" : "NOT_FOUND", "Target error") };
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);
        var csrf = await client.GetFromJsonAsync<CsrfBody>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        var target = Guid.NewGuid();
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"/api/blocks/{target}"));
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(1, fake.Calls);
        Assert.Equal(Caller, fake.CurrentUser);
        Assert.Equal(target, fake.TargetUser);
        if (status == 204) Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        else
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
            Assert.Equal(fake.Error!.Code, error!.Code);
            Assert.Equal(fake.Error.Message, error.Message);
        }
    }

    [Fact]
    public async Task InvalidGuid_ReturnsValidationJson()
    {
        var fake = new FakeBlocks();
        using var factory = Factory(fake);
        using var client = factory.CreateClient();
        Authenticate(factory, client);
        var csrf = await client.GetFromJsonAsync<CsrfBody>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        var response = await client.PutAsync("/api/blocks/not-a-guid", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task SelfBlock_IsRejectedBeforeDatabaseAccess()
    {
        using var db = new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>().Options);
        var service = new BlockService(db);
        var error = await Assert.ThrowsAsync<BlockException>(() => service.BlockAsync(Caller, Caller));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("SELF_BLOCK_NOT_ALLOWED", error.Code);
    }

    private sealed record ErrorBody(string Code, string Message);
    private sealed record CsrfBody(string Token);
}
