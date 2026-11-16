using Chat.Client.Models;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.AspNetCore.SignalR.Client;
namespace Chat.Client.Services;

public sealed class ChatHubClient(HttpClient http) : IAsyncDisposable
{
    private HubConnection? connection;
    private readonly HashSet<Guid> subscriptions = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;
    public event Func<ConversationChanged, Task>? ConversationChanged;
    public event Func<Task>? Reconnected;
    public event Func<HubConnectionState, Task>? StatusChanged;
    public event Func<Exception, Task>? SubscriptionFailed;
    public HubConnectionState State => connection?.State ?? HubConnectionState.Disconnected;

    public async Task StartAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed) return;
            if (connection is null)
            {
                connection = new HubConnectionBuilder()
                    .WithUrl(new Uri(http.BaseAddress!, "hubs/chat"), options =>
                        options.HttpMessageHandlerFactory = inner => new CookieHandler { InnerHandler = inner })
                    .WithAutomaticReconnect()
                    .Build();
                connection.On<ConversationChanged>("ConversationChanged", async change =>
                {
                    if (!disposed && ConversationChanged is { } handlers)
                        foreach (Func<ConversationChanged, Task> handler in handlers.GetInvocationList()) await handler(change);
                });
                connection.Reconnecting += _ => NotifyAsync(HubConnectionState.Reconnecting);
                connection.Closed += _ => NotifyAsync(HubConnectionState.Disconnected);
                connection.Reconnected += async _ =>
                {
                    try
                    {
                        await RejoinAsync();
                    }
                    finally
                    {
                        await NotifyAsync(State);
                        if (!disposed && Reconnected is { } handlers)
                            foreach (Func<Task> handler in handlers.GetInvocationList()) await handler();
                    }
                };
            }
            if (State == HubConnectionState.Disconnected)
            {
                await NotifyAsync(HubConnectionState.Connecting);
                await connection.StartAsync();
                await RejoinAsync();
            }
            await NotifyAsync(State);
        }
        catch { await NotifyAsync(State); throw; }
        finally { gate.Release(); }
    }

    public async Task JoinAsync(Guid id)
    {
        await gate.WaitAsync();
        try
        {
            if (disposed || !subscriptions.Add(id)) return;
            if (State == HubConnectionState.Connected)
                try { await connection!.InvokeAsync("JoinConversation", id.ToString("D")); }
                catch { subscriptions.Remove(id); throw; }
        }
        finally { gate.Release(); }
    }

    private async Task NotifyAsync(HubConnectionState state)
    {
        if (!disposed && StatusChanged is { } handlers)
            foreach (Func<HubConnectionState, Task> handler in handlers.GetInvocationList()) await handler(state);
    }

    private async Task RejoinAsync()
    {
        foreach (var id in subscriptions.ToArray())
        {
            if (disposed) return;
            try { await connection!.InvokeAsync("JoinConversation", id.ToString("D")); }
            catch (Exception ex)
            {
                // One revoked conversation must not stop reconnecting the remaining subscriptions.
                subscriptions.Remove(id);
                if (SubscriptionFailed is { } handlers)
                    foreach (Func<Exception, Task> handler in handlers.GetInvocationList()) await handler(ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        await gate.WaitAsync();
        try
        {
            if (connection is not null) await connection.DisposeAsync();
            connection = null;
            subscriptions.Clear();
        }
        finally { gate.Release(); }
    }

    private sealed class CookieHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
            return base.SendAsync(request, ct);
        }
    }
}
