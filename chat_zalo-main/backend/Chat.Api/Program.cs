using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ChatDb")
    ?? "Host=localhost;Port=5432;Database=chat_zalo;Username=chat_zalo;Password=dev_change_me";

builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDataProtection();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<ChatDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();