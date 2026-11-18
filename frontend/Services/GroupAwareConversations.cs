using Chat.Client.Models;

namespace Chat.Client.Services;

// Bọc quanh API danh sách hội thoại để chat hiểu thêm về NHÓM:
//  - Backend trả nhóm trong GET /api/conversations nhưng KHÔNG kèm tên nhóm, nên hỏi thêm từng nhóm để lấy tên.
//  - Backend vẫn trả cả nhóm đã giải tán hoặc nhóm mình đã rời, nên nhóm nào hỏi ra bị 403/404 thì ẩn đi.
// Kết quả hỏi được ghi nhớ ngắn hạn để không hỏi lại mỗi 5 giây.
public sealed class GroupAwareConversations(IChatConversations inner, IGroupApiClient groups) : IChatConversations
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(30);

    // Name = null nghĩa là nhóm không còn truy cập được (đã giải tán / đã rời)
    private readonly Dictionary<Guid, (string? Name, DateTime CheckedAt)> known = new();

    // Tên các nhóm còn truy cập được, để trang chat hiển thị
    public IReadOnlyDictionary<Guid, string> GroupNames =>
        known.Where(k => k.Value.Name is not null).ToDictionary(k => k.Key, k => k.Value.Name!);

    public async Task<List<ConversationDto>> GetAsync()
    {
        var all = await inner.GetAsync();
        var result = new List<ConversationDto>();

        foreach (var conversation in all)
        {
            if (conversation.Kind != "Group")
            {
                result.Add(conversation);
                continue;
            }

            // Còn trong thời gian ghi nhớ thì dùng lại kết quả cũ, hết hạn thì hỏi lại
            if (!known.TryGetValue(conversation.Id, out var entry) || DateTime.UtcNow - entry.CheckedAt > CacheTime)
            {
                try
                {
                    var group = await groups.GetAsync(conversation.Id);
                    entry = (group.Name, DateTime.UtcNow);
                }
                catch (ApiException ex) when (ex.StatusCode is 403 or 404)
                {
                    entry = (null, DateTime.UtcNow);
                }

                known[conversation.Id] = entry;
            }

            if (entry.Name is not null)
                result.Add(conversation);
        }

        return result;
    }
}
