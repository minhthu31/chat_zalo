using Chat.Client;
using Chat.Client.Services;
using Chat.Client.State;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddScoped<ChatApiClient>();
builder.Services.AddScoped<ConversationsApiClient>();
builder.Services.AddScoped<MessagesApiClient>();
builder.Services.AddScoped<ChatHubClient>();
builder.Services.AddScoped<ChatState>();

await builder.Build().RunAsync();
