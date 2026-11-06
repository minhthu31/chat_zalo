using Chat.Client.Models.Messages;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chat.Client.Services;

public sealed class ChatHubClient : IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly HashSet<Guid> _joined = [];
    public event Func<ConversationChangedEvent, Task>? ConversationChanged;
    public event Func<Task>? Reconnected;
    public event Func<Exception?, Task>? Closed;
    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    public async Task StartAsync(Uri apiBaseUri, CancellationToken ct = default)
    {
        if (_connection is not null && State is HubConnectionState.Connected or HubConnectionState.Connecting)
            return;

        _connection ??= new HubConnectionBuilder()
            .WithUrl(new Uri(apiBaseUri, "hubs/chat"))
            .WithAutomaticReconnect()
            .Build();

        _connection.On<ConversationChangedEvent>("ConversationChanged", async evt =>
        {
            var handlers = ConversationChanged;
            if (handlers is null) return;
            foreach (Func<ConversationChangedEvent, Task> handler in handlers.GetInvocationList())
                await handler(evt);
        });
        _connection.Reconnected += async _ =>
        {
            // Server-side group membership is connection-scoped: rejoin every active conversation.
            foreach (var id in _joined.ToArray())
                await _connection.InvokeAsync("JoinConversation", id.ToString());
            if (Reconnected is not null) await Reconnected.Invoke();
        };
        _connection.Closed += async error =>
        {
            _joined.Clear();
            if (Closed is not null) await Closed.Invoke(error);
        };
        await _connection.StartAsync(ct);
    }

    public async Task JoinConversationAsync(Guid id, CancellationToken ct = default)
    {
        if (_connection?.State != HubConnectionState.Connected || _joined.Contains(id)) return;
        await _connection.InvokeAsync("JoinConversation", id.ToString(), ct);
        _joined.Add(id);
    }

    public async Task LeaveConversationAsync(Guid id, CancellationToken ct = default)
    {
        if (_connection?.State == HubConnectionState.Connected && _joined.Contains(id))
            await _connection.InvokeAsync("LeaveConversation", id.ToString(), ct);
        _joined.Remove(id);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _connection = null;
        _joined.Clear();
    }
}
