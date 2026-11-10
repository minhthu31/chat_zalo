using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chat.Application.DTOs.Users;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chat.Users.Tests;

public sealed class PagedUsersTests
{
    private static readonly Guid CallerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string CallerUsername = "paged_test_caller";
    private const string CallerSecurityStamp = "SECRET_STAMP_paged_test_caller";

    private static ApplicationUser CreateUser(Guid id, string username, string displayName)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            Email = $"{username}@secret.example.com",
            NormalizedEmail = $"{username.ToUpperInvariant()}@SECRET.EXAMPLE.COM",
            PasswordHash = $"SECRET_HASH_{username}",
            SecurityStamp = $"SECRET_STAMP_{username}",
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            PhoneNumber = "0900123456",
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static List<ApplicationUser> CreateSeededUsers(int count, string prefix)
    {
        var rng = new Random(42);
        return Enumerable.Range(1, count)
            .Select(i => CreateUser(
                Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
                $"{prefix}_{i:D3}",
                $"User {prefix} {i:D3}"))
            .OrderBy(_ => rng.Next())
            .ToList();
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IReadOnlyList<ApplicationUser> users,
        List<string>? executedSqlLog = null)
    {
        // SecurityStampValidator reloads the authenticated caller from the Identity store.
        // Include that caller in the interceptor's in-memory user set so the cookie
        // represents a real test user with a matching Security Stamp.
        var testUsers = users
            .Append(CreateUser(CallerId, CallerUsername, "Paged Test Caller"))
            .ToList();

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ChatDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ChatDbContext>>();
                services.RemoveAll<ChatDbContext>();

                services.AddDbContext<ChatDbContext>(options =>
                    options.UseNpgsql("Host=localhost;Database=chat_unit_sim;Username=sim;Password=sim")
                        .AddInterceptors(
                            new SuppressConnectionInterceptor(),
                            new PagedUsersCommandInterceptor(testUsers, executedSqlLog)));
            }));
    }

    private static void Authenticate(WebApplicationFactory<Program> factory, HttpClient client, Guid userId)
    {
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var identityOptions = factory.Services
            .GetRequiredService<IOptions<IdentityOptions>>()
            .Value;
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(identityOptions.ClaimsIdentity.SecurityStampClaimType, CallerSecurityStamp)
            },
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

    // -----------------------------------------------------------------------
    // Test 1: Kiểm tra response phân trang
    // -----------------------------------------------------------------------
    [Fact]
    public async Task SearchUsersPaged_ValidRequest_ReturnsExpectedContractFieldsAndValues()
    {
        var users = CreateSeededUsers(25, "alice");
        using var factory = CreateFactory(users);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var response = await client.GetAsync("/api/users/search/paged?username=alice&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        var propertyNames = root.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "items", "page", "pageSize", "totalCount", "totalPages" }, propertyNames);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("items").ValueKind);
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(10, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(25, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, root.GetProperty("totalPages").GetInt32());

        var dto = await response.Content.ReadFromJsonAsync<PagedUsersResponse>();
        Assert.NotNull(dto);
        Assert.Equal(10, dto!.Items.Count);
        Assert.Equal(1, dto.Page);
        Assert.Equal(10, dto.PageSize);
        Assert.Equal(25, dto.TotalCount);
        Assert.Equal(3, dto.TotalPages);
        Assert.Equal("alice_001", dto.Items[0].Username);
        Assert.Equal("User alice 001", dto.Items[0].DisplayName);
    }

    // -----------------------------------------------------------------------
    // Test 2: Kiểm tra pageSize (1, 10, 100)
    // -----------------------------------------------------------------------
    [Theory]
    [InlineData(1, 1, 237)]
    [InlineData(10, 10, 24)]
    [InlineData(100, 100, 3)]
    public async Task SearchUsersPaged_VariousPageSizes_RespectsLimitAndComputesTotalPages(
        int pageSize,
        int expectedItemsOnFirstPage,
        int expectedTotalPages)
    {
        var users = CreateSeededUsers(237, "member");
        using var factory = CreateFactory(users);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var response = await client.GetAsync(
            $"/api/users/search/paged?username=member&page=1&pageSize={pageSize}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedUsersResponse>();
        Assert.NotNull(body);
        Assert.Equal(expectedItemsOnFirstPage, body!.Items.Count);
        Assert.True(body.Items.Count <= body.TotalCount);
        Assert.Equal(1, body.Page);
        Assert.Equal(pageSize, body.PageSize);
        Assert.Equal(237, body.TotalCount);
        Assert.Equal(expectedTotalPages, body.TotalPages);
    }

    // -----------------------------------------------------------------------
    // Test 3: Kiểm tra dữ liệu giữa các trang (không trùng lặp, không bỏ sót)
    // -----------------------------------------------------------------------
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public async Task SearchUsersPaged_AcrossPages_HasNoDuplicatesAndNoMissingUsers(int pageSize)
    {
        var users = CreateSeededUsers(237, "pageduser");
        var expectedOrderedIds = users
            .OrderBy(u => u.UserName, StringComparer.Ordinal)
            .ThenBy(u => u.Id)
            .Select(u => u.Id)
            .ToList();

        using var factory = CreateFactory(users);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var page1 = await client.GetFromJsonAsync<PagedUsersResponse>(
            $"/api/users/search/paged?username=pageduser&page=1&pageSize={pageSize}");
        var page2 = await client.GetFromJsonAsync<PagedUsersResponse>(
            $"/api/users/search/paged?username=pageduser&page=2&pageSize={pageSize}");

        Assert.NotNull(page1);
        Assert.NotNull(page2);
        Assert.Empty(page1!.Items.Select(u => u.Id).Intersect(page2!.Items.Select(u => u.Id)));

        var collectedIds = new List<Guid>();
        for (var page = 1; page <= page1.TotalPages; page++)
        {
            var current = await client.GetFromJsonAsync<PagedUsersResponse>(
                $"/api/users/search/paged?username=pageduser&page={page}&pageSize={pageSize}");
            Assert.NotNull(current);
            collectedIds.AddRange(current!.Items.Select(u => u.Id));
        }

        Assert.Equal(237, collectedIds.Count);
        Assert.Equal(237, collectedIds.Distinct().Count());
        Assert.Equal(expectedOrderedIds, collectedIds);
    }

    // -----------------------------------------------------------------------
    // Test 4: Kiểm tra sắp xếp ổn định (gọi lặp nhiều lần + tie-breaker theo Id)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task SearchUsersPaged_RepeatedCallsAndDuplicateUsernames_MaintainsStableOrderById()
    {
        var users = CreateSeededUsers(25, "stable");
        var tieLowId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var tieHighId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        users.Add(CreateUser(tieHighId, "stable_tie", "Tie High"));
        users.Add(CreateUser(tieLowId, "stable_tie", "Tie Low"));

        using var factory = CreateFactory(users);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        List<Guid>? baselineIds = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var page = await client.GetFromJsonAsync<PagedUsersResponse>(
                "/api/users/search/paged?username=stable&page=2&pageSize=10");
            Assert.NotNull(page);
            var currentIds = page!.Items.Select(u => u.Id).ToList();

            if (baselineIds is null)
            {
                baselineIds = currentIds;
            }
            else
            {
                Assert.Equal(baselineIds, currentIds);
            }
        }

        var tieResult = await client.GetFromJsonAsync<PagedUsersResponse>(
            "/api/users/search/paged?username=stable_tie&page=1&pageSize=10");
        Assert.NotNull(tieResult);
        Assert.Equal(2, tieResult!.Items.Count);
        Assert.Equal(tieLowId, tieResult.Items[0].Id);
        Assert.Equal(tieHighId, tieResult.Items[1].Id);
    }

    // -----------------------------------------------------------------------
    // Test 5: Kiểm tra trang cuối
    // -----------------------------------------------------------------------
    [Fact]
    public async Task SearchUsersPaged_LastPage_ReturnsRemainingItemsExactly()
    {
        var users = CreateSeededUsers(237, "lastpage");
        using var factory = CreateFactory(users);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var lastPageSize10 = await client.GetFromJsonAsync<PagedUsersResponse>(
            "/api/users/search/paged?username=lastpage&page=24&pageSize=10");
        Assert.NotNull(lastPageSize10);
        Assert.Equal(24, lastPageSize10!.Page);
        Assert.Equal(10, lastPageSize10.PageSize);
        Assert.Equal(237, lastPageSize10.TotalCount);
        Assert.Equal(24, lastPageSize10.TotalPages);
        Assert.Equal(7, lastPageSize10.Items.Count);
        Assert.Equal("lastpage_231", lastPageSize10.Items[0].Username);
        Assert.Equal("lastpage_237", lastPageSize10.Items[6].Username);

        var lastPageSize100 = await client.GetFromJsonAsync<PagedUsersResponse>(
            "/api/users/search/paged?username=lastpage&page=3&pageSize=100");
        Assert.NotNull(lastPageSize100);
        Assert.Equal(37, lastPageSize100!.Items.Count);
        Assert.Equal("lastpage_201", lastPageSize100.Items[0].Username);
        Assert.Equal("lastpage_237", lastPageSize100.Items[36].Username);
    }

    // -----------------------------------------------------------------------
    // Test 6: Kiểm tra trang không có kết quả
    // -----------------------------------------------------------------------
    [Fact]
    public async Task SearchUsersPaged_NoMatchingUsers_ReturnsEmptyItemsWithZeroTotals()
    {
        var sqlLog = new List<string>();
        var users = CreateSeededUsers(15, "existing");
        using var factory = CreateFactory(users, sqlLog);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var response = await client.GetAsync(
            "/api/users/search/paged?username=nobody_matches_this_keyword&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedUsersResponse>();
        Assert.NotNull(result);
        Assert.Empty(result!.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.Single(sqlLog);
    }

    // -----------------------------------------------------------------------
    // Test 7: Kiểm tra int.MaxValue
    // -----------------------------------------------------------------------
    [Fact]
    public async Task SearchUsersPaged_IntMaxValuePage_DoesNotOverflowAndReturnsEmptyItems()
    {
        var sqlLog = new List<string>();
        var users = CreateSeededUsers(25, "overflowcheck");
        using var factory = CreateFactory(users, sqlLog);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var response = await client.GetAsync(
            $"/api/users/search/paged?username=overflowcheck&page={int.MaxValue}&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedUsersResponse>();
        Assert.NotNull(result);
        Assert.Empty(result!.Items);
        Assert.Equal(int.MaxValue, result.Page);
        Assert.Equal(100, result.PageSize);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Single(sqlLog);
    }

    // -----------------------------------------------------------------------
    // Test 8: Kiểm tra tham số không hợp lệ
    // -----------------------------------------------------------------------
    [Theory]
    [InlineData("username=alice&page=-1&pageSize=10", "INVALID_PAGE")]
    [InlineData("username=alice&page=-100&pageSize=10", "INVALID_PAGE")]
    [InlineData("username=alice&page=0&pageSize=10", "INVALID_PAGE")]
    [InlineData("username=alice&page=1&pageSize=-1", "INVALID_PAGE_SIZE")]
    [InlineData("username=alice&page=1&pageSize=-50", "INVALID_PAGE_SIZE")]
    [InlineData("username=alice&page=1&pageSize=0", "INVALID_PAGE_SIZE")]
    [InlineData("username=alice&page=1&pageSize=101", "INVALID_PAGE_SIZE")]
    [InlineData("username=alice&page=1&pageSize=500", "INVALID_PAGE_SIZE")]
    [InlineData("username=alice&page=false&pageSize=10", "VALIDATION_ERROR")]
    [InlineData("username=alice&page=1&pageSize=false", "VALIDATION_ERROR")]
    [InlineData("username=alice&page=abc&pageSize=10", "VALIDATION_ERROR")]
    [InlineData("page=1&pageSize=10", "USERNAME_REQUIRED")]
    [InlineData("username=&page=1&pageSize=10", "USERNAME_REQUIRED")]
    [InlineData("username=%20%20%20&page=1&pageSize=10", "USERNAME_REQUIRED")]
    public async Task SearchUsersPaged_InvalidParameters_Returns400WithExpectedErrorCode(
        string queryString,
        string expectedCode)
    {
        var users = CreateSeededUsers(10, "alice");
        using var factory = CreateFactory(users);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var response = await client.GetAsync($"/api/users/search/paged?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(error);
        Assert.Equal(expectedCode, error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public async Task SearchUsersPaged_UsernameExceeds100Characters_Returns400InvalidUsername()
    {
        var users = CreateSeededUsers(5, "alice");
        using var factory = CreateFactory(users);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var tooLongUsername = new string('a', 101);
        var response = await client.GetAsync(
            $"/api/users/search/paged?username={tooLongUsername}&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(error);
        Assert.Equal("INVALID_USERNAME", error!.Code);
    }

    // -----------------------------------------------------------------------
    // Test 9: Kiểm tra xác thực (chưa đăng nhập trả 401)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task SearchUsersPaged_UnauthenticatedRequest_Returns401UnauthorizedWithoutRedirect()
    {
        var users = CreateSeededUsers(10, "alice");
        using var factory = CreateFactory(users);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/users/search/paged?username=alice&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var error = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(error);
        Assert.Equal("UNAUTHORIZED", error!.Code);
    }

    // -----------------------------------------------------------------------
    // Test 10: Kiểm tra bảo mật dữ liệu (chỉ chứa id, username, displayName)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task SearchUsersPaged_ResponseJson_ExposesOnlyAllowedFieldsAndNoSensitiveData()
    {
        var users = CreateSeededUsers(15, "secureuser");
        using var factory = CreateFactory(users);
        using var client = factory.CreateClient();
        Authenticate(factory, client, CallerId);

        var response = await client.GetAsync("/api/users/search/paged?username=secureuser&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawJson = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("email", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("concurrencyStamp", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phoneNumber", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET_HASH_", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET_STAMP_", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret.example.com", rawJson, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(rawJson);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(10, items.GetArrayLength());

        foreach (var item in items.EnumerateArray())
        {
            var keys = item.EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "id", "username", "displayName" }, keys);
        }
    }

    private sealed record ErrorBody(string Code, string Message);

    private sealed class SuppressConnectionInterceptor : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result) => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());

        public override InterceptionResult ConnectionClosing(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result) => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionClosingAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class PagedUsersCommandInterceptor : DbCommandInterceptor
    {
        private readonly IReadOnlyList<ApplicationUser> _users;
        private readonly List<string>? _sqlLog;

        public PagedUsersCommandInterceptor(
            IReadOnlyList<ApplicationUser> users,
            List<string>? sqlLog)
        {
            _users = users;
            _sqlLog = sqlLog;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result) =>
            InterceptionResult<DbDataReader>.SuppressWithResult(Execute(command));

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(Execute(command)));

        private DbDataReader Execute(DbCommand cmd)
        {
            var sql = cmd.CommandText;

            // SecurityStampValidator first reloads the authenticated user, then
            // asks Identity for that user's claims. These are not paging queries.
            if (sql.Contains("FROM \"Users\" AS u", StringComparison.Ordinal) &&
                sql.Contains("WHERE u.\"Id\" = @p", StringComparison.Ordinal))
            {
                return ExecuteIdentityUserLookup(cmd);
            }

            if (sql.Contains("FROM \"UserClaims\" AS u", StringComparison.Ordinal))
            {
                var claimsTable = new DataTable();
                claimsTable.Columns.Add("Id", typeof(int));
                claimsTable.Columns.Add("ClaimType", typeof(string));
                claimsTable.Columns.Add("ClaimValue", typeof(string));
                claimsTable.Columns.Add("UserId", typeof(Guid));
                return claimsTable.CreateDataReader();
            }

            // AddRoles<IdentityRole<Guid>>() causes SecurityStampValidator to
            // query UserRoles (joined with Roles) when refreshing the principal.
            // Return an empty result so the user simply has no roles assigned.
            if (sql.Contains("\"UserRoles\"", StringComparison.Ordinal))
            {
                var rolesTable = new DataTable();
                rolesTable.Columns.Add("Name", typeof(string));
                return rolesTable.CreateDataReader();
            }

            // Only the Users username search queries belong to this paging
            // interceptor. Do not interpret unrelated Identity SQL as LIKE SQL.
            if (!sql.Contains("FROM \"Users\" AS u", StringComparison.Ordinal) ||
                !sql.Contains("LIKE", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"PagedUsersCommandInterceptor received an unsupported SQL query: {sql}");
            }

            _sqlLog?.Add(sql);

            var patternParameter = cmd.Parameters
                .Cast<DbParameter>()
                .FirstOrDefault(parameter => parameter.Value is string);

            if (patternParameter?.Value is not string pattern)
            {
                throw new InvalidOperationException(
                    "Paged users query did not contain a string LIKE parameter.");
            }

            var regex = LikePatternToRegex(pattern);
            IEnumerable<ApplicationUser> matching = _users
                .Where(u => u.UserName != null && regex.IsMatch(u.UserName));

            if (sql.StartsWith("SELECT count(*)", StringComparison.OrdinalIgnoreCase))
            {
                var countTable = new DataTable();
                countTable.Columns.Add("count", typeof(int));
                countTable.Rows.Add(matching.Count());
                return countTable.CreateDataReader();
            }

            if (sql.Contains("ORDER BY u.\"UserName\", u.\"Id\"", StringComparison.Ordinal))
            {
                matching = matching
                    .OrderBy(u => u.UserName, StringComparer.Ordinal)
                    .ThenBy(u => u.Id);
            }
            else if (sql.Contains("ORDER BY u.\"UserName\"", StringComparison.Ordinal))
            {
                matching = matching.OrderBy(u => u.UserName, StringComparer.Ordinal);
            }

            var offset = ReadIntClause(sql, cmd, "OFFSET");
            var limit = ReadIntClause(sql, cmd, "LIMIT");
            if (offset is { } o) matching = matching.Skip(o);
            if (limit is { } l) matching = matching.Take(l);

            var resultTable = new DataTable();
            resultTable.Columns.Add("Id", typeof(Guid));
            resultTable.Columns.Add("UserName", typeof(string));
            resultTable.Columns.Add("DisplayName", typeof(string));

            foreach (var user in matching)
            {
                resultTable.Rows.Add(user.Id, user.UserName, user.DisplayName);
            }

            return resultTable.CreateDataReader();
        }

        private DbDataReader ExecuteIdentityUserLookup(DbCommand cmd)
        {
            var idParameter = cmd.Parameters
                .Cast<DbParameter>()
                .FirstOrDefault(parameter => parameter.Value is Guid);
            var requestedId = idParameter?.Value is Guid id ? id : Guid.Empty;
            var user = _users.SingleOrDefault(candidate => candidate.Id == requestedId);

            var table = new DataTable();
            table.Columns.Add("Id", typeof(Guid));
            table.Columns.Add("AccessFailedCount", typeof(int));
            table.Columns.Add("ConcurrencyStamp", typeof(string));
            table.Columns.Add("CreatedAt", typeof(DateTimeOffset));
            table.Columns.Add("DisplayName", typeof(string));
            table.Columns.Add("Email", typeof(string));
            table.Columns.Add("EmailConfirmed", typeof(bool));
            table.Columns.Add("LockoutEnabled", typeof(bool));
            table.Columns.Add("LockoutEnd", typeof(DateTimeOffset));
            table.Columns.Add("NormalizedEmail", typeof(string));
            table.Columns.Add("NormalizedUserName", typeof(string));
            table.Columns.Add("PasswordHash", typeof(string));
            table.Columns.Add("PhoneNumber", typeof(string));
            table.Columns.Add("PhoneNumberConfirmed", typeof(bool));
            table.Columns.Add("SecurityStamp", typeof(string));
            table.Columns.Add("TwoFactorEnabled", typeof(bool));
            table.Columns.Add("UserName", typeof(string));

            if (user is not null)
            {
                table.Rows.Add(
                    user.Id,
                    user.AccessFailedCount,
                    (object?)user.ConcurrencyStamp ?? DBNull.Value,
                    user.CreatedAt,
                    (object?)user.DisplayName ?? DBNull.Value,
                    (object?)user.Email ?? DBNull.Value,
                    user.EmailConfirmed,
                    user.LockoutEnabled,
                    user.LockoutEnd.HasValue ? (object)user.LockoutEnd.Value : DBNull.Value,
                    (object?)user.NormalizedEmail ?? DBNull.Value,
                    (object?)user.NormalizedUserName ?? DBNull.Value,
                    (object?)user.PasswordHash ?? DBNull.Value,
                    (object?)user.PhoneNumber ?? DBNull.Value,
                    user.PhoneNumberConfirmed,
                    (object?)user.SecurityStamp ?? DBNull.Value,
                    user.TwoFactorEnabled,
                    (object?)user.UserName ?? DBNull.Value);
            }

            return table.CreateDataReader();
        }

        private static int? ReadIntClause(string sql, DbCommand cmd, string keyword)
        {
            var match = Regex.Match(sql, keyword + @"\s+(@\w+|\d+)");
            if (!match.Success) return null;

            var token = match.Groups[1].Value;
            if (!token.StartsWith('@')) return int.Parse(token);

            var param = cmd.Parameters
                .Cast<DbParameter>()
                .Single(p => "@" + p.ParameterName.TrimStart('@') == token);
            return Convert.ToInt32(param.Value);
        }

        private static Regex LikePatternToRegex(string pattern)
        {
            var sb = new StringBuilder("^");
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                if (c == '\\' && i + 1 < pattern.Length)
                {
                    sb.Append(Regex.Escape(pattern[++i].ToString()));
                }
                else if (c == '%')
                {
                    sb.Append(".*");
                }
                else if (c == '_')
                {
                    sb.Append('.');
                }
                else
                {
                    sb.Append(Regex.Escape(c.ToString()));
                }
            }

            sb.Append('$');
            return new Regex(
                sb.ToString(),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }
    }
}