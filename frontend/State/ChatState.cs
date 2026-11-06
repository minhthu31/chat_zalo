using Chat.Client.Models.Conversations;
using Chat.Client.Models.Messages;

namespace Chat.Client.State;

public sealed class ChatState
{
    public bool UseMockData { get; set; }
    public IReadOnlyList<ConversationDto> Conversations { get; private set; } = [];
    public List<MessageResponse> Messages { get; } = [];
    public Guid? ActiveConversationId { get; private set; }
    public long LastSequence => Messages.Count == 0 ? 0 : Messages.Max(x => x.Seq);
    public long? OldestSequence => Messages.Count == 0 ? null : Messages.Min(x => x.Seq);

    public void SetConversations(IReadOnlyList<ConversationDto> items)
    {
        Conversations = items;
        if (ActiveConversationId is null && items.Count > 0) ActiveConversationId = items[0].Id;
    }

    public void SelectConversation(Guid id) { ActiveConversationId = id; Messages.Clear(); }
    public void ReplaceMessages(IEnumerable<MessageResponse> items) { Messages.Clear(); MergeMessages(items); }

    public void MergeMessages(IEnumerable<MessageResponse> incoming)
    {
        var ids = Messages.Select(x => x.Id).ToHashSet();
        var seqs = Messages.Select(x => x.Seq).ToHashSet();
        foreach (var item in incoming.OrderBy(x => x.Seq))
            if (ids.Add(item.Id) && seqs.Add(item.Seq)) Messages.Add(item);
        Messages.Sort((a,b) => a.Seq.CompareTo(b.Seq));
    }

    public static IReadOnlyList<ConversationDto> MockConversations =>
    [
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Direct", 4),
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Direct", 2)
    ];

    public static IReadOnlyList<MessageResponse> MockMessages(Guid id) =>
    [
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), id, Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"), 1, "Chào bạn! Bạn đang làm phần giao diện chat đúng không?", DateTimeOffset.UtcNow.AddMinutes(-20)),
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), id, Guid.Parse("cccccccc-0000-0000-0000-000000000001"), 2, "Đúng rồi, mình đang hoàn thiện danh sách hội thoại.", DateTimeOffset.UtcNow.AddMinutes(-15)),
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), id, Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"), 3, "Nhớ đồng bộ theo afterSeq và tải lịch sử bằng beforeSeq nhé.", DateTimeOffset.UtcNow.AddMinutes(-8)),
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"), id, Guid.Parse("cccccccc-0000-0000-0000-000000000001"), 4, "Mình đã xử lý phần đó rồi!", DateTimeOffset.UtcNow.AddMinutes(-3))
    ];
}
