// Chỉ là cấu hình để khởi động project; KHÔNG chứa nghiệp vụ chat.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
app.Run();
