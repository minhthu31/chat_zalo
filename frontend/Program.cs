using Chat.Client;
using Chat.Client.Services;
using Chat.Client.State;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"];

if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiUri) ||
    (apiUri.Scheme != Uri.UriSchemeHttp &&
     apiUri.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException(
        "Cấu hình ApiBaseUrl trong wwwroot/appsettings.json phải là URL HTTP/HTTPS hợp lệ.");
}

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = apiUri
});

builder.Services.AddScoped<ApiRequestSender>();
builder.Services.AddScoped<IAuthApiClient, AuthApiClient>();
builder.Services.AddScoped<IUserApiClient, UserApiClient>();
builder.Services.AddScoped<IFriendshipApiClient, FriendshipApiClient>();
builder.Services.AddScoped<IBlockApiClient, BlockApiClient>();
builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<ChatApiClient>();
builder.Services.AddTransient<ChatRealtimeClient>();

// Sprint 3 DI: enable each registration when its interface and implementation exist.
// builder.Services.AddScoped<IProfileApiClient, ProfileApiClient>();
// builder.Services.AddScoped<IAttachmentApiClient, AttachmentApiClient>();
// builder.Services.AddScoped<IConversationSettingsApiClient, ConversationSettingsApiClient>();
// builder.Services.AddScoped<IMessageReceiptApiClient, MessageReceiptApiClient>();

await builder.Build().RunAsync();
