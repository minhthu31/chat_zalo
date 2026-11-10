using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

// Separate one-shot process: API startup never changes the database implicitly.
// No development fallback: the deployment must supply its database explicitly.
var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__ChatDb")
    ?? throw new InvalidOperationException("Configure the migration database connection.");
await using var db = new ChatDbContext(new DbContextOptionsBuilder<ChatDbContext>()
    .UseNpgsql(connection).Options);
await db.Database.MigrateAsync();
Console.WriteLine("Database migrations applied successfully.");
