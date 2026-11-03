using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Chat.Application.DTOs.Users;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Chat.Users.Tests;

// ---------------------------------------------------------------------------
// CHAT-03: In-memory stub IUserAccountGateway for unit/HTTP tests without DB.
// ---------------------------------------------------------------------------
internal sealed class FakeUserAccountGateway : IUserAccountGateway
{
    private readonly Dictionary<string, UserDto> _store = new();

    public int FindByIdCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int SearchCalls { get; private set; }
    public string? LastUpdatedUserId { get; private set; }
    public string? LastSearchedUsername { get; private set; }

    public void Seed(Guid id, string username, string displayName)
    {
        _store[id.ToString()] = new UserDto
        {
            Id = id,
            Username = username,
            DisplayName = displayName
        };
    }

    public Task<UserDto?> FindByIdAsync(string userId)
    {
        FindByIdCalls++;
        _store.TryGetValue(userId, out var dto);
        return Task.FromResult(dto);
    }

    public Task<UserDto?> UpdateDisplayNameAsync(string userId, string displayName)
    {
        UpdateCalls++;
        LastUpdatedUserId = userId;
        if (!_store.TryGetValue(userId, out var dto))
            return Task.FromResult<UserDto?>(null);

        var updated = new UserDto
        {
            Id = dto.Id,
            Username = dto.Username,
            DisplayName = displayName
        };
        _store[userId] = updated;
        return Task.FromResult<UserDto?>(updated);
    }

    public Task<IReadOnlyList<UserDto>> SearchByUsernameAsync(string username)
    {
        SearchCalls++;
        LastSearchedUsername = username;
        IReadOnlyList<UserDto> results = _store.Values
            .Where(u => u.Username.Contains(username, StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.Username)
            .Take(20)
            .ToList();
        return Task.FromResult(results);
    }
}

// ===========================================================================
// 1. Unit Tests cho UserService (IUserService) & UserDto Guid contract
// ===========================================================================
public sealed class UserServiceTests
{
    private readonly FakeUserAccountGateway _gateway;
    private readonly UserService _sut;

    private static readonly Guid ExistingUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThirdUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid MissingUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public UserServiceTests()
    {
        _gateway = new FakeUserAccountGateway();
        _gateway.Seed(ExistingUserId, "alice", "Alice Nguyen");
        _gateway.Seed(SecondUserId, "alice_b", "Alice B");
        _gateway.Seed(ThirdUserId, "bob", "Bob Tran");
        _sut = new UserService(_gateway);
    }

    [Fact]
    public async Task GetProfile_ExistingUser_ReturnsCorrectDtoWithGuidId()
    {
        var result = await _sut.GetProfileAsync(ExistingUserId);
        Assert.Equal(ExistingUserId, result.Id);
        Assert.Equal("alice", result.Username);
        Assert.Equal("Alice Nguyen", result.DisplayName);
    }

    [Fact]
    public async Task GetProfile_UserDtoDoesNotHaveEmailProperty()
    {
        var dto = await _sut.GetProfileAsync(ExistingUserId);
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(ExistingUserId.ToString(), doc.RootElement.GetProperty("id").GetString());
        Assert.False(doc.RootElement.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task GetProfile_NonExistingUser_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.GetProfileAsync(MissingUserId));
    }

    [Fact]
    public async Task UpdateProfile_ValidDisplayName_ReturnsUpdatedDto()
    {
        var req = new UpdateProfileRequest { DisplayName = "Alice Updated" };
        var result = await _sut.UpdateProfileAsync(ExistingUserId, req);
        Assert.Equal("Alice Updated", result.DisplayName);
        Assert.Equal(ExistingUserId, result.Id);
        Assert.Equal("alice", result.Username);
    }

    [Fact]
    public async Task UpdateProfile_TrimsLeadingTrailingWhitespace_EvenWhenRawLengthExceeds100()
    {
        var padded = "   " + new string('A', 100) + "   ";
        var req = new UpdateProfileRequest { DisplayName = padded };
        var result = await _sut.UpdateProfileAsync(ExistingUserId, req);
        Assert.Equal(100, result.DisplayName.Length);
    }

    [Fact]
    public async Task UpdateProfile_WhitespaceDisplayName_ThrowsArgumentException()
    {
        var req = new UpdateProfileRequest { DisplayName = "   " };
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpdateProfileAsync(ExistingUserId, req));
    }

    [Fact]
    public async Task UpdateProfile_TooLongDisplayName_ThrowsArgumentException()
    {
        var req = new UpdateProfileRequest { DisplayName = new string('A', 101) };
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateProfileAsync(ExistingUserId, req));
        Assert.Contains("100", ex.Message);
    }

    [Fact]
    public async Task UpdateProfile_NullRequest_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateProfileAsync(ExistingUserId, null!));
    }

    [Fact]
    public async Task UpdateProfile_NonExistingUser_ThrowsKeyNotFound()
    {
        var req = new UpdateProfileRequest { DisplayName = "Ghost" };
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UpdateProfileAsync(MissingUserId, req));
    }

    [Fact]
    public async Task UpdateProfile_OnlyAffectsTargetUser_NotOthers()
    {
        await _sut.UpdateProfileAsync(ExistingUserId, new UpdateProfileRequest { DisplayName = "Alice Renamed" });
        var bob = await _sut.GetProfileAsync(ThirdUserId);
        Assert.Equal("Bob Tran", bob.DisplayName);
    }

    [Fact]
    public async Task Search_MatchingUsername_ReturnsResults()
    {
        var results = await _sut.SearchAsync("alice");
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Contains("alice", r.Username, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmptyList()
    {
        var results = await _sut.SearchAsync("nonexistentxyz");
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_Results_DoNotHaveEmailOrSensitivePropertiesInJson()
    {
        var results = await _sut.SearchAsync("alice");
        Assert.NotEmpty(results);
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_EmptyUsername_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SearchAsync(""));
    }

    [Fact]
    public async Task Search_WhitespaceUsername_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SearchAsync("   "));
    }

    [Fact]
    public async Task Search_TooLongUsername_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.SearchAsync(new string('a', 101)));
    }
}

// ===========================================================================
// 2. HTTP Regression Tests cho UsersController (Cookie Auth, CSRF, AuthZ, JSON)
// ===========================================================================
public sealed class UsersApiTests
{
    private static readonly Guid CallerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static WebApplicationFactory<Program> CreateFactory(FakeUserAccountGateway gateway) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserAccountGateway>();
                services.AddScoped<IUserAccountGateway>(_ => gateway);
            }));

    private static FakeUserAccountGateway CreateSeededGateway()
    {
        var gateway = new FakeUserAccountGateway();
        gateway.Seed(CallerId, "caller_user", "Caller Original");
        gateway.Seed(OtherUserId, "other_user", "Other Original");
        return gateway;
    }

    private static void Authenticate(WebApplicationFactory<Program> factory, HttpClient client, Guid userId)
    {
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
            IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
            },
            IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
    }

    [Theory]
    [InlineData("GET", "/api/users/me")]
    [InlineData("PATCH", "/api/users/me")]
    [InlineData("GET", "/api/users/search?username=caller")]
    public async Task Anonymous_Request_Returns401Json_WithoutRedirect(string method, string path)
    {
        using var factory = CreateFactory(CreateSeededGateway());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PATCH")
        {
            request.Content = JsonContent.Create(new { displayName = "Hacker" });
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(error);
        Assert.Equal("UNAUTHORIZED", error!.Code);
    }

    [Fact]
    public async Task GetCurrentUser_Authenticated_ReturnsOnlyCurrentUser_IgnoresQueryId()
    {
        var gateway = CreateSeededGateway();
        using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var response = await client.GetAsync($"/api/users/me?id={OtherUserId}&userId={OtherUserId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(dto);
        Assert.Equal(CallerId, dto!.Id);
        Assert.Equal("caller_user", dto.Username);
        Assert.Equal("Caller Original", dto.DisplayName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateProfile_MissingOrInvalidCsrfToken_Returns400InvalidCsrfToken(bool sendInvalidHeader)
    {
        var gateway = CreateSeededGateway();
        using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        if (sendInvalidHeader)
        {
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "invalid-token");
        }

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/users/me")
        {
            Content = JsonContent.Create(new { displayName = "Should Fail" })
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(error);
        Assert.Equal("INVALID_CSRF_TOKEN", error!.Code);
        Assert.Equal(0, gateway.UpdateCalls);
    }

    [Fact]
    public async Task UpdateProfile_ValidCsrf_UpdatesOnlyCaller_AndIgnoresTamperedBodyFields()
    {
        var gateway = CreateSeededGateway();
        using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var csrf = await client.GetFromJsonAsync<CsrfBody>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);

        var tamperedJson = JsonSerializer.Serialize(new
        {
            id = OtherUserId,
            username = "hacked_username",
            email = "hacked@example.com",
            roles = new[] { "Admin" },
            displayName = "  Caller Updated Name  "
        });

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/users/me")
        {
            Content = new StringContent(tamperedJson, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(updated);
        Assert.Equal(CallerId, updated!.Id);
        Assert.Equal("caller_user", updated.Username);
        Assert.Equal("Caller Updated Name", updated.DisplayName);

        Assert.Equal(CallerId.ToString(), gateway.LastUpdatedUserId);
        var otherUser = await gateway.FindByIdAsync(OtherUserId.ToString());
        Assert.Equal("Other Original", otherUser!.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task UpdateProfile_InvalidDisplayName_Returns400InvalidDisplayName(string invalidDisplayName)
    {
        var gateway = CreateSeededGateway();
        using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var csrf = await client.GetFromJsonAsync<CsrfBody>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/users/me")
        {
            Content = JsonContent.Create(new { displayName = invalidDisplayName })
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(error);
        Assert.Equal("INVALID_DISPLAY_NAME", error!.Code);
        Assert.Equal(0, gateway.UpdateCalls);
    }

    [Fact]
    public async Task GetAndUpdateProfile_NonExistingUser_Return404UserNotFound()
    {
        var gateway = CreateSeededGateway();
        var missingUserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authenticate(factory, client, missingUserId);

        var getResponse = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal("USER_NOT_FOUND", (await getResponse.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var csrf = await client.GetFromJsonAsync<CsrfBody>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, "/api/users/me")
        {
            Content = JsonContent.Create(new { displayName = "Valid Name" })
        };

        var patchResponse = await client.SendAsync(patchRequest);
        Assert.Equal(HttpStatusCode.NotFound, patchResponse.StatusCode);
        Assert.Equal("USER_NOT_FOUND", (await patchResponse.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task SearchUsers_ValidationAndEmptyResult_ReturnsExpectedStatusAndCodes()
    {
        var gateway = CreateSeededGateway();
        using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var missingQuery = await client.GetAsync("/api/users/search");
        Assert.Equal(HttpStatusCode.BadRequest, missingQuery.StatusCode);
        Assert.Equal("USERNAME_REQUIRED", (await missingQuery.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var whitespaceQuery = await client.GetAsync("/api/users/search?username=%20%20%20");
        Assert.Equal(HttpStatusCode.BadRequest, whitespaceQuery.StatusCode);
        Assert.Equal("USERNAME_REQUIRED", (await whitespaceQuery.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var tooLongQuery = await client.GetAsync($"/api/users/search?username={new string('x', 101)}");
        Assert.Equal(HttpStatusCode.BadRequest, tooLongQuery.StatusCode);
        Assert.Equal("INVALID_USERNAME", (await tooLongQuery.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var emptyResult = await client.GetAsync("/api/users/search?username=nobody_matches_this");
        Assert.Equal(HttpStatusCode.OK, emptyResult.StatusCode);
        var emptyList = await emptyResult.Content.ReadFromJsonAsync<List<UserDto>>();
        Assert.NotNull(emptyList);
        Assert.Empty(emptyList!);

        var matchResult = await client.GetAsync("/api/users/search?username=other");
        Assert.Equal(HttpStatusCode.OK, matchResult.StatusCode);
        var rawJson = await matchResult.Content.ReadAsStringAsync();
        Assert.DoesNotContain("email", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", rawJson, StringComparison.OrdinalIgnoreCase);
        var matchList = JsonSerializer.Deserialize<List<UserDto>>(
            rawJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Single(matchList!);
        Assert.Equal(OtherUserId, matchList![0].Id);
    }

    private sealed record ErrorBody(string Code, string Message);
    private sealed record CsrfBody(string Token);
}

// ===========================================================================
// 3. PostgreSQL 18 Integration Tests cho tìm kiếm ký tự '_', '%', '\'
// Chạy với PostgreSQL 18 thử nghiệm qua biến môi trường CHAT_TEST_CONNECTION_STRING.
// ===========================================================================
public sealed class UsersPostgresTests
{
    private static string GetVerifiedTestConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("A verified PostgreSQL test connection is required.");
        var builder = new NpgsqlConnectionStringBuilder(cs);
        var databaseName = builder.Database ?? string.Empty;
        if (!databaseName.Contains("_test", StringComparison.OrdinalIgnoreCase)
            && !databaseName.Contains("_review", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Use a dedicated test database whose name contains _test or _review.");
        }

        return cs;
    }

    [Fact]
    public async Task SearchByUsername_EscapesUnderscore_Percent_Backslash_And_ParameterizesSql()
    {
        var cs = GetVerifiedTestConnectionString();
        await using var db = new ChatDbContext(
            new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(cs).Options);
        await db.Database.OpenConnectionAsync();
        if (((NpgsqlConnection)db.Database.GetDbConnection()).PostgreSqlVersion.Major != 18)
        {
            throw new InvalidOperationException("PostgreSQL 18 is required.");
        }

        var tag = Guid.NewGuid().ToString("N")[..8];
        var seededUsers = new[]
        {
            CreateUser($"{tag}_a_b", "Literal Underscore"),
            CreateUser($"{tag}_axb", "Wildcard Underscore Decoy"),
            CreateUser($"{tag}_100%real", "Literal Percent"),
            CreateUser($"{tag}_100abc_real", "Wildcard Percent Decoy"),
            CreateUser($"{tag}_back\\slash", "Literal Backslash"),
            CreateUser($"{tag}_backslash", "Backslash Decoy")
        };

        db.Users.AddRange(seededUsers);
        await db.SaveChangesAsync();

        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<ChatDbContext>>();
                    services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(cs));
                }));

            using var scope = factory.Services.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

            var underscoreResults = await userService.SearchAsync($"{tag}_a_b");
            Assert.Single(underscoreResults);
            Assert.Equal($"{tag}_a_b", underscoreResults[0].Username);
            Assert.Equal(seededUsers[0].Id, underscoreResults[0].Id);

            var percentResults = await userService.SearchAsync($"{tag}_100%real");
            Assert.Single(percentResults);
            Assert.Equal($"{tag}_100%real", percentResults[0].Username);
            Assert.Equal(seededUsers[2].Id, percentResults[0].Id);

            var backslashResults = await userService.SearchAsync($"{tag}_back\\slash");
            Assert.Single(backslashResults);
            Assert.Equal($"{tag}_back\\slash", backslashResults[0].Username);
            Assert.Equal(seededUsers[4].Id, backslashResults[0].Id);

            var injectionAttempt = await userService.SearchAsync($"{tag}' OR 1=1 --");
            Assert.Empty(injectionAttempt);
        }
        finally
        {
            var ids = seededUsers.Select(u => u.Id).ToArray();
            await db.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
        }
    }

    private static ApplicationUser CreateUser(string username, string displayName)
    {
        var id = Guid.NewGuid();
        return new ApplicationUser
        {
            Id = id,
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            Email = $"{id:N}@test.local",
            NormalizedEmail = $"{id:N}@TEST.LOCAL",
            DisplayName = displayName
        };
    }
}
