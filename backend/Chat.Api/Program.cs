using Chat.Api.Controllers;
using Chat.Application.DTOs.Auth;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Humanizer.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Read local configuration or override with environment variables.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration.GetConnectionString("ChatDb")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:ChatDb or ConnectionStrings:DefaultConnection.");

builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDataProtection();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<ChatDbContext>()
.AddDefaultTokenProviders()
.AddSignInManager();

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
var cookieExpirationHours = configuration.GetValue<int?>("Authentication:CookieExpirationHours") ?? 8;

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
builder.Services.AddScoped<IAccountGateway, IdentityAccountGateway>();


builder.Services.AddControllers();

// OpenAPI chuẩn .NET 10
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

// Middleware order: CORS -> Authentication -> Authorization
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();
