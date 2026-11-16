using Chat.Client.Models;
namespace Chat.Client.State;

public sealed class ConversationState
{
    private readonly SortedDictionary<long, MessageResponse> messages = [];
    public IReadOnlyList<MessageResponse> Messages => messages.Values.ToList();
    // Do not advance this cursor from a POST response: missing messages may precede it.
    public long SyncedSeq { get; internal set; }
    public long? FirstSeq => messages.Count == 0 ? null : messages.Keys.First();
    public bool Initialized { get; internal set; }
    public bool HasOlder { get; internal set; }
    public bool Loading { get; internal set; }
    public bool Sending { get; internal set; }
    public string Draft { get; set; } = "";
    public SendMessageRequest? Pending { get; internal set; }
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    public void Merge(Guid conversationId, IEnumerable<MessageResponse> incoming)
    {
        var ids = messages.Values.Select(m => m.Id).ToHashSet();
        foreach (var item in incoming.Where(m => m.ConversationId == conversationId).OrderBy(m => m.Seq))
            if (!messages.ContainsKey(item.Seq) && ids.Add(item.Id)) messages.Add(item.Seq, item);
    }
}

public sealed class ChatState
{
    private readonly Dictionary<Guid, ConversationState> conversations = [];
    public Guid? SelectedId { get; set; }
    public ConversationState? Active => SelectedId is Guid id ? For(id) : null;
    public ConversationState For(Guid id)
    {
        if (!conversations.TryGetValue(id, out var state)) conversations[id] = state = new();
        return state;
    }
    public void Clear() { SelectedId = null; conversations.Clear(); }
}
