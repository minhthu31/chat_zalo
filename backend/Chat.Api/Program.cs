using System.Threading.RateLimiting;
using Chat.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Chat.Api.Controllers;
using Chat.Api.Authorization;
using Chat.Application.DTOs.Auth;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Infrastructure.Data;
using chat_zalo.Hubs;
using chat_zalo.Services;
using Chat.Infrastructure.Identity;
using Chat.Infrastructure.Services;
using Humanizer.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Read local configuration or override with environment variables.
var databaseUrl = configuration["DATABASE_URL"];
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration.GetConnectionString("ChatDb");

if (!string.IsNullOrWhiteSpace(databaseUrl))
{
    var database = new Uri(databaseUrl);
    var credentials = database.UserInfo.Split(':', 2);
    connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = database.Host,
        Port = database.Port > 0 ? database.Port : 5432,
        Database = Uri.UnescapeDataString(database.AbsolutePath.TrimStart('/')),
        Username = Uri.UnescapeDataString(credentials[0]),
        Password = Uri.UnescapeDataString(credentials[1]),
        SslMode = SslMode.Require
    }.ConnectionString;
}

builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseNpgsql(connectionString
        ?? throw new InvalidOperationException("Configure DATABASE_URL or a ChatDb connection string.")));

var dataProtection = builder.Services.AddDataProtection();
var keyPath = configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(keyPath))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var address in configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(address));
});

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<ChatDbContext>()
.AddDefaultTokenProviders()
.AddSignInManager();

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    // CHAT-13: validate the Identity Security Stamp on every authenticated request
    // so cookies issued before a password change are rejected on their next request.
    options.ValidationInterval = TimeSpan.Zero;
});

var cookieName = configuration["Authentication:CookieName"] ?? "Chat.Auth";
var antiforgeryCookieName = configuration["Authentication:AntiforgeryCookieName"]
    ?? "Chat.Antiforgery";
var sameSiteText = configuration["Authentication:SameSite"];
var sameSite = Enum.TryParse<SameSiteMode>(sameSiteText, ignoreCase: true, out var parsedSameSite)
    ? parsedSameSite
    : SameSiteMode.Lax;

var securePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

var cookieExpirationHours = builder.Configuration.GetValue<int?>("Authentication:CookieExpirationHours") ?? 8;

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    })
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = cookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = sameSite;
        options.Cookie.SecurePolicy = securePolicy;
        options.ExpireTimeSpan = TimeSpan.FromHours(cookieExpirationHours);
        options.SlidingExpiration = true;

        // Revalidate the Identity security stamp on every authenticated request.
        // This makes cookies issued before a password change invalid immediately.
        options.Events.OnValidatePrincipal =
            SecurityStampValidator.ValidatePrincipalAsync;

        // API clients receive JSON/status codes instead of browser login redirects.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsJsonAsync(new AuthErrorResponse
            {
                Code = "UNAUTHORIZED",
                Message = "Bạn chưa đăng nhập."
            });
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsJsonAsync(new AuthErrorResponse
            {
                Code = "FORBIDDEN",
                Message = "Bạn không có quyền thực hiện thao tác này."
            });
        };
    })
    // Identity's security-stamp rejection signs out the remember-me 2FA cookie too.
    // AddIdentityCore does not register Identity cookie handlers automatically.
    .AddCookie(IdentityConstants.TwoFactorRememberMeScheme, options =>
    {
        options.Cookie.Name = $"{cookieName}.TwoFactorRememberMe";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = sameSite;
        options.Cookie.SecurePolicy = securePolicy;
    });

builder.Services.AddAuthorization();

// CSRF contract: GET /api/auth/csrf returns the request token in JSON;
// unsafe requests send it back in the X-CSRF-TOKEN header.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = antiforgeryCookieName;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = sameSite;
    options.Cookie.SecurePolicy = securePolicy;
});

// CORS is ready for a future browser frontend. Keep the list empty until its
// exact origin is known; never combine credentialed requests with wildcard origins.
var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

// S1-02 application abstractions
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IConversationService, Chat.Infrastructure.Services.ConversationService>();
builder.Services.AddScoped<IBlockService, Chat.Infrastructure.Services.BlockService>();
builder.Services.AddScoped<IFriendshipService, Chat.Infrastructure.Services.FriendshipService>();
builder.Services.AddScoped<IAccountGateway, IdentityAccountGateway>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserAccountGateway, IdentityUserAccountGateway>();
builder.Services.AddScoped<IMessageService, Chat.Infrastructure.Services.MessageService>();
builder.Services.AddScoped<IGroupService, Chat.Infrastructure.Services.GroupService>();

builder.Services.AddSingleton<PasswordRecovery>();
builder.Services.AddHostedService(services => services.GetRequiredService<PasswordRecovery>());
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("password-recovery", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(5)
        }));
});

builder.Services.AddControllers();

builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .SelectMany(e => e.Value!.Errors.Select(err => err.ErrorMessage))
            .ToList();

        var message = errors.Count > 0
            ? string.Join(" ", errors)
            : "Dữ liệu gửi lên không hợp lệ.";

        return new BadRequestObjectResult(new AuthErrorResponse
        {
            Code = "VALIDATION_ERROR",
            Message = message

        });
    };
});
// OpenAPI chuẩn .NET 10
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Đăng ký DI bắt buộc cho CHAT-8
builder.Services.AddSingleton<IChatConnectionTracker, ChatConnectionTracker>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ChatNotificationService>();
builder.Services.AddScoped<IChatNotificationService>(services =>
    services.GetRequiredService<ChatNotificationService>());
builder.Services.AddScoped<IConversationEventPublisher>(services =>
    services.GetRequiredService<ChatNotificationService>());
builder.Services.AddScoped<IReceiptEventPublisher>(services =>
    services.GetRequiredService<ChatNotificationService>());

   builder.Services.AddGroupPermissions();
var webRoot = Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
builder.Services.AddOptions<FileStorageOptions>().Bind(configuration.GetSection("FileStorage"))
    .Validate(options => Path.IsPathFullyQualified(options.RootPath)
        && !Path.GetFullPath(options.RootPath).TrimEnd(Path.DirectorySeparatorChar).Equals(webRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
        && !Path.GetFullPath(options.RootPath).StartsWith(webRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
        "FileStorage:RootPath must be an absolute private directory outside wwwroot.")
    .ValidateOnStart();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<IPendingFileCleanupService, PendingFileCleanupService>();



// Sprint 3 DI
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IAvatarService, AvatarService>();
builder.Services.AddScoped<IAttachmentMessageService, AttachmentMessageService>();
builder.Services.AddScoped<IConversationSettingsService, ConversationSettingsService>();
builder.Services.AddScoped<IMessageReceiptService, MessageReceiptService>();


var app = builder.Build();
var serveClient = configuration.GetValue<bool>("ServeClient");

// Manual operator command; this mode does not start an HTTP server.
if (configuration.GetValue<bool>("cleanup-pending"))
{
    if (!DateTimeOffset.TryParse(configuration["cleanup-before"], System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AssumeUniversal, out var cutoff))
        throw new ArgumentException("Provide --cleanup-before with an ISO-8601 UTC cutoff.");
    using var scope = app.Services.CreateScope();
    var removed = await scope.ServiceProvider.GetRequiredService<IPendingFileCleanupService>()
        .CleanupAsync(cutoff.ToUniversalTime(), configuration.GetValue<int?>("cleanup-limit") ?? 100);
    Console.WriteLine($"Removed {removed} unused Pending files.");
    return;
}

app.UseForwardedHeaders();
if (serveClient)
{
    app.UseStaticFiles(new StaticFileOptions { ServeUnknownFileTypes = true });
}
app.UseRouting();

// Middleware order: CORS -> Authentication -> Authorization
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
if (serveClient)
{
    app.MapFallbackToFile("index.html");
}

if (configuration.GetValue<bool>("MigrateOnStart"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ChatDbContext>().Database.MigrateAsync();
}

app.Run();
