using Chat.Client.Models;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chat.Client.Services;

public sealed class ChatRealtimeClient(HttpClient http) : IAsyncDisposable
{
    private HubConnection? connection;
    public event Func<ConversationChanged, Task>? Changed;
    public event Func<Task>? Reconnected;
    public event Func<string, Task>? StatusChanged;

    public async Task StartAsync()
    {
        if (connection is not null)
        {
            if (connection.State == HubConnectionState.Disconnected) await connection.StartAsync();
            return;
        }
        connection = new HubConnectionBuilder()
            .WithUrl(new Uri(http.BaseAddress!, "hubs/chat"), options =>
                options.HttpMessageHandlerFactory = inner => new CookieHandler { InnerHandler = inner })
            .WithAutomaticReconnect()
            .Build();
        connection.On<ConversationChanged>("ConversationChanged", async change =>
        {
            if (Changed is not null) await Changed(change);
        });
        connection.Reconnected += async _ =>
        {
            if (StatusChanged is not null) await StatusChanged("Đã kết nối");
            if (Reconnected is not null) await Reconnected();
        };
        connection.Reconnecting += async _ =>
        {
            if (StatusChanged is not null) await StatusChanged("Đang kết nối lại…");
        };
        connection.Closed += async _ =>
        {
            if (StatusChanged is not null) await StatusChanged("Mất kết nối — hãy kết nối lại");
        };
        try { await connection.StartAsync(); }
        catch { await DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        var current = connection;
        connection = null;
        if (current is not null) await current.DisposeAsync();
    }

    private sealed class CookieHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
