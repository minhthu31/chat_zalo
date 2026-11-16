using Chat.Client.Models;
using Chat.Client.Services;
namespace Chat.Client.State;

public sealed class ChatSession(IChatMessages api)
{
    public ChatState State { get; } = new();
    private int generation;
    private bool IsCurrent(Guid id, ConversationState state, int version) =>
        generation == version && ReferenceEquals(State.For(id), state);
    public void Reset() { generation++; State.Clear(); }

    public async Task SyncAsync(Guid id)
    {
        var state = State.For(id);
        var version = generation;
        await state.Gate.WaitAsync();
        try
        {
            if (!IsCurrent(id, state, version)) return;
            state.Loading = true;
            if (!state.Initialized)
            {
                var latest = await api.GetLatestAsync(id);
                if (!IsCurrent(id, state, version)) return;
                state.Merge(id, latest);
                state.SyncedSeq = latest.Where(m => m.ConversationId == id).Select(m => m.Seq).DefaultIfEmpty().Max();
                state.HasOlder = latest.Count == 20;
                state.Initialized = true;
            }
            while (IsCurrent(id, state, version))
            {
                var cursor = state.SyncedSeq;
                var batch = await api.GetNewAsync(id, cursor);
                if (!IsCurrent(id, state, version)) return;
                var valid = batch.Where(m => m.ConversationId == id && m.Seq > cursor).ToList();
                state.Merge(id, valid);
                if (valid.Count > 0) state.SyncedSeq = valid.Max(m => m.Seq);
                if (batch.Count < 100) break;
                if (state.SyncedSeq <= cursor) throw new InvalidOperationException("Messages API không tiến được mốc đồng bộ.");
            }
        }
        finally { state.Loading = false; state.Gate.Release(); }
    }

    public async Task LoadOlderAsync(Guid id)
    {
        var state = State.For(id);
        var version = generation;
        await state.Gate.WaitAsync();
        try
        {
            if (!IsCurrent(id, state, version) || !state.HasOlder || state.FirstSeq is not long before) return;
            state.Loading = true;
            var batch = await api.GetOlderAsync(id, before);
            if (!IsCurrent(id, state, version)) return;
            state.Merge(id, batch);
            state.HasOlder = batch.Count == 20;
        }
        finally { state.Loading = false; state.Gate.Release(); }
    }

    public async Task SendAsync(Guid id)
    {
        var state = State.For(id);
        if (state.Sending || !state.Initialized || (state.Pending is null && string.IsNullOrWhiteSpace(state.Draft))) return;
        if (state.Draft.Length > 4000) throw new InvalidOperationException("Tin nhắn tối đa 4.000 ký tự.");
        var version = generation;
        state.Pending ??= new SendMessageRequest(Guid.NewGuid(), state.Draft);
        var request = state.Pending;
        state.Sending = true;
        try
        {
            var sent = await api.SendAsync(id, request);
            if (!IsCurrent(id, state, version)) return;
            state.Merge(id, [sent]);
            state.Pending = null;
            state.Draft = "";
        }
        catch (ApiException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            if (IsCurrent(id, state, version)) state.Pending = null;
            throw;
        }
        finally { state.Sending = false; }
        if (IsCurrent(id, state, version)) await SyncAsync(id);
    }
}
