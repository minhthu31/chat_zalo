using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using Chat.Infrastructure.Identity;
using Chat.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chat.Users.Tests;

public sealed class AuthChangePasswordApiTests
{
    private const string InitialPassword = "Initial_Pass@2026";
    private const string NewPassword = "Changed_Pass@2026";

    private sealed record CsrfBody(string Token);
    private sealed record ErrorBody(string Code, string Message);

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var connectionString = Environment.GetEnvironmentVariable("CHAT_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Set CHAT_TEST_CONNECTION_STRING to the dedicated chat_zalo_test PostgreSQL database with migrations applied.");

        // Fail closed: this fixture creates users and changes passwords, so it must never
        // run against a development or production database. Keep this allowlist aligned
        // with the dedicated database used by the repository's CI workflow.
        var parsedConnectionString = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };
        if (!parsedConnectionString.TryGetValue("Database", out var databaseValue) ||
            !string.Equals(Convert.ToString(databaseValue), "chat_zalo_test", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "CHAT_TEST_CONNECTION_STRING must target the dedicated 'chat_zalo_test' database. Refusing to run change-password integration tests against any other database.");
        }

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Keep both supported keys aligned. Program.cs currently prefers DefaultConnection,
            // while appsettings.json in this repository defines ChatDb.
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["ConnectionStrings:ChatDb"] = connectionString
                }));

            // Override the DbContext options after the application's registrations. This makes
            // the test database target explicit instead of relying only on configuration-key
            // precedence inside Program.cs.
            builder.ConfigureTestServices(services =>
                services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(connectionString)));
        });

        // Verify the connection string on the actual DbContext used by UserManager/Identity,
        // not merely IConfiguration. This prevents integration tests from touching chat_zalo.
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var actualConnectionString = db.Database.GetDbConnection().ConnectionString;
            var actualBuilder = new DbConnectionStringBuilder
            {
                ConnectionString = actualConnectionString
            };

            if (!actualBuilder.TryGetValue("Database", out var actualDatabase) ||
                !string.Equals(Convert.ToString(actualDatabase), "chat_zalo_test", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The ChatDbContext used by the integration tests is not targeting the exact 'chat_zalo_test' database. Refusing to run change-password integration tests.");
            }

            if (!actualBuilder.TryGetValue("Port", out var actualPort) ||
                !string.Equals(Convert.ToString(actualPort), Convert.ToString(parsedConnectionString["Port"]), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The ChatDbContext is not using the PostgreSQL port configured by CHAT_TEST_CONNECTION_STRING. Refusing to run change-password integration tests.");
            }
        }
        catch
        {
            factory.Dispose();
            throw;
        }

        return factory;
    }

    private static async Task<ApplicationUser> CreateUserAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();

        // The integration-test database is explicitly allowlisted in CreateFactory().
        // Apply the application's existing migrations there before Identity creates a user.
        // This prepares test infrastructure only; it does not add or modify migrations.
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        await db.Database.MigrateAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var id = Guid.NewGuid();
        var username = $"change_password_{id:N}";
        var user = new ApplicationUser
        {
            Id = id,
            UserName = username,
            Email = $"{username}@example.test",
            DisplayName = "Change password test",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await users.CreateAsync(user, InitialPassword);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        return user;
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<CsrfBody>("/api/auth/csrf");
        Assert.NotNull(token);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!.Token);
        return token.Token;
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password)
    {
        await GetCsrfAsync(client);
        return await client.PostAsJsonAsync("/api/auth/login", new
        {
            usernameOrEmail = username,
            password
        });
    }

    private static async Task LoginSuccessfullyAsync(HttpClient client, string username, string password)
    {
        using var response = await LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_Anonymous_Returns401()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await GetCsrfAsync(client);

        using var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = InitialPassword,
            newPassword = NewPassword
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task ChangePassword_MissingCsrf_Returns400()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory);
        using var client = factory.CreateClient();
        await LoginSuccessfullyAsync(client, user.UserName!, InitialPassword);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");

        using var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = InitialPassword,
            newPassword = NewPassword
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_CSRF_TOKEN", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task ChangePassword_InvalidCsrf_Returns400()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory);
        using var client = factory.CreateClient();
        await LoginSuccessfullyAsync(client, user.UserName!, InitialPassword);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "not-a-valid-antiforgery-token");

        using var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = InitialPassword,
            newPassword = NewPassword
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_CSRF_TOKEN", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ReturnsStable400Error()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory);
        using var client = factory.CreateClient();
        await LoginSuccessfullyAsync(client, user.UserName!, InitialPassword);
        await GetCsrfAsync(client);

        using var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "Wrong_Pass@2026",
            newPassword = NewPassword
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_CURRENT_PASSWORD", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task ChangePassword_WeakNewPassword_ReturnsValidationError()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory);
        using var client = factory.CreateClient();
        await LoginSuccessfullyAsync(client, user.UserName!, InitialPassword);
        await GetCsrfAsync(client);

        using var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = InitialPassword,
            newPassword = "weak"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    [Fact]
    public async Task ChangePassword_Success_SignsOutCurrentSession_RevokesOlderSession_AndNewPasswordWorks()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory);
        using var currentDevice = factory.CreateClient();
        using var otherDevice = factory.CreateClient();
        await LoginSuccessfullyAsync(currentDevice, user.UserName!, InitialPassword);
        await LoginSuccessfullyAsync(otherDevice, user.UserName!, InitialPassword);

        // Both clients now hold independently issued, real Identity cookies for the same user.
        Assert.Equal(HttpStatusCode.OK, (await currentDevice.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherDevice.GetAsync("/api/auth/me")).StatusCode);

        await GetCsrfAsync(currentDevice);
        using (var changeResponse = await currentDevice.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = InitialPassword,
            newPassword = NewPassword
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);
            Assert.Empty(await changeResponse.Content.ReadAsByteArrayAsync());

            var cookieName = factory.Services
                .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(IdentityConstants.ApplicationScheme).Cookie.Name;
            Assert.True(changeResponse.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders));
            Assert.Contains(setCookieHeaders!, header =>
                header.StartsWith($"{cookieName}=", StringComparison.OrdinalIgnoreCase)
                && header.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await currentDevice.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/auth/me")).StatusCode);

        using var newDevice = factory.CreateClient();
        using (var newPasswordLogin = await LoginAsync(newDevice, user.UserName!, NewPassword))
            Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);

        using var oldPasswordDevice = factory.CreateClient();
        using (var oldPasswordLogin = await LoginAsync(oldPasswordDevice, user.UserName!, InitialPassword))
            Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);
    }

}
