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
builder.Services.AddScoped<AuthState>();

await builder.Build().RunAsync();