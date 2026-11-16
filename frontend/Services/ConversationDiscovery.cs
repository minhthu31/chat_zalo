using Chat.Client.Models;
namespace Chat.Client.Services;

// The v1.2 hub only sends events for joined rooms. Discover rooms through REST first.
public sealed class ConversationDiscovery(IChatConversations api, Func<Guid, Task> subscribe)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public event Func<Exception, Task>? SubscriptionFailed;

    public async Task<List<ConversationDto>> RefreshAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var conversations = (await api.GetAsync().WaitAsync(ct)).Where(c => c.Kind == "Direct").ToList();
            foreach (var conversation in conversations)
            {
                ct.ThrowIfCancellationRequested();
                try { await subscribe(conversation.Id).WaitAsync(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    if (SubscriptionFailed is { } handlers)
                        foreach (Func<Exception, Task> handler in handlers.GetInvocationList()) await handler(ex);
                }
            }
            ct.ThrowIfCancellationRequested();
            return conversations;
        }
        finally { gate.Release(); }
    }
}
