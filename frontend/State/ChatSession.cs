using Chat.Client.Models;
using Chat.Client.Services;
namespace Chat.Client.State;

// pageSize phải bằng số tin mỗi lần của MessagesApiClient, vì "đủ pageSize tin" nghĩa là có thể còn tin cũ hơn
public sealed class ChatSession(IChatMessages api, int pageSize = 20)
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
                state.HasOlder = latest.Count == pageSize;
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

    // Re-query the loaded window: Blocks may change without new message seqs.
    public async Task RefreshVisibleAsync(Guid id)
    {
        var state = State.For(id);
        var version = generation;
        await state.Gate.WaitAsync();
        try
        {
            if (!IsCurrent(id, state, version)) return;
            state.Loading = true;
            var first = state.FirstSeq;
            var complete = state.Initialized && !state.HasOlder;
            var all = await api.GetLatestAsync(id);
            var batch = all.ToList();
            while (batch.Count == pageSize && batch.Count > 0
                && (complete || (first.HasValue && batch.Min(m => m.Seq) > first.Value)))
            {
                var cursor = batch.Min(m => m.Seq);
                batch = await api.GetOlderAsync(id, cursor);
                if (batch.Any(m => m.Seq >= cursor))
                    throw new InvalidOperationException("History cursor did not advance.");
                all.AddRange(batch);
            }
            if (!IsCurrent(id, state, version)) return;
            state.Replace(id, all);
            state.SyncedSeq = all.Where(m => m.ConversationId == id).Select(m => m.Seq).DefaultIfEmpty().Max();
            state.HasOlder = batch.Count == pageSize;
            state.Initialized = true;
        }
        catch (ApiException ex) when (ex.StatusCode is 401 or 403)
        {
            if (IsCurrent(id, state, version))
            {
                state.Replace(id, []); state.Initialized = false; state.HasOlder = false; state.SyncedSeq = 0;
            }
            throw;
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
            state.HasOlder = batch.Count == pageSize;
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
