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
            var first = state.VisibilityStartSeq ?? state.FirstSeq;
            var complete = state.Initialized && !state.HasOlder;
            var all = new List<MessageResponse>();
            bool hasOlder;
            if (state.Initialized && (first.HasValue || complete))
            {
                var cursor = complete ? 0 : Math.Max(0, first!.Value - 1);
                hasOlder = !complete && cursor > 0;
                while (true)
                {
                    var batch = await api.GetNewAsync(id, cursor);
                    if (!IsCurrent(id, state, version)) return;
                    var valid = batch.Where(m => m.ConversationId == id && m.Seq > cursor).ToList();
                    all.AddRange(valid);
                    if (batch.Count < 100) break;
                    var next = valid.Select(m => m.Seq).DefaultIfEmpty(cursor).Max();
                    if (next <= cursor) throw new InvalidOperationException("Messages cursor did not advance.");
                    cursor = next;
                }
            }
            else
            {
                all = await api.GetLatestAsync(id);
                hasOlder = all.Count == pageSize;
            }
            if (!IsCurrent(id, state, version)) return;
            state.Replace(id, all);
            state.VisibilityStartSeq = complete ? 0 : first ?? state.FirstSeq;
            state.SyncedSeq = all.Where(m => m.ConversationId == id).Select(m => m.Seq).DefaultIfEmpty().Max();
            state.HasOlder = hasOlder;
            state.Initialized = true;
        }
        catch (ApiException ex) when (ex.StatusCode is 401 or 403)
        {
            if (IsCurrent(id, state, version))
            {
                state.Replace(id, []); state.VisibilityStartSeq = null; state.Initialized = false; state.HasOlder = false; state.SyncedSeq = 0;
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
            if (state.FirstSeq is long first)
                state.VisibilityStartSeq = Math.Min(state.VisibilityStartSeq ?? first, first);
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
