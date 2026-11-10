using Chat.Client;
using Chat.Client.Services;
using Chat.Client.State;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
// Deployments use '/' so REST and SignalR follow the website's public origin.
// Local development can continue to use an absolute backend URL.
if (apiBaseUrl == "/")
    apiBaseUrl = builder.HostEnvironment.BaseAddress;

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
builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<ChatApiClient>();
builder.Services.AddTransient<ChatRealtimeClient>();

await builder.Build().RunAsync();
