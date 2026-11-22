using Chat.Client.Models;
using Chat.Client.Services;

namespace Chat.Client.State;

// Phần "đã gửi / đã nhận / đã xem" của trò chuyện, tách riêng khỏi ChatSession:
//  - tải và cập nhật trạng thái của các thành viên (API receipts + sự kiện SignalR MessageReceiptChanged);
//  - báo lên backend khi MÌNH đã nhận / đã xem tin;
//  - tính chữ hiển thị dưới tin của mình.
// Hai cách tính theo hợp đồng CHAT-28:
//  - Chat cá nhân: dùng MỐC (seq) "đã nhận tới tin số..", "đã xem tới tin số.." và mốc chỉ tăng.
//  - Chat nhóm: xác nhận TỪNG TIN (PUT seq = đúng một tin), vì người ở trong nhóm có thể bị chặn nên không thấy một số tin.
public sealed class ReceiptCoordinator(ChatSession session, IMessageReceiptApiClient api)
{
    private const int PageIds = 100;          // hợp đồng: GET receipts nhận tối đa 100 mã tin
    private const int MaxConfirmPerPass = 30; // mỗi lượt chỉ báo tối đa chừng này tin (tránh bắn quá nhiều yêu cầu một lúc)

    // Tải trạng thái. Lỗi thì bỏ qua: trạng thái chỉ là phụ, tin nhắn vẫn dùng bình thường.
    // Nhóm: kèm mã các tin đang có để lấy trạng thái từng tin.
    public async Task LoadAsync(Guid conversationId, bool isGroup, Guid myId)
    {
        var state = session.State.For(conversationId);
        var x = state.Extras();

        try
        {
            IReadOnlyCollection<Guid>? ids = null;
            if (isGroup)
            {
                ids = state.Messages.OrderByDescending(m => m.Seq).Take(PageIds).Select(m => m.Id).ToList();
                if (ids.Count == 0) ids = null;
            }

            var result = await api.GetAsync(conversationId, ids);

            // Người đã rời nhóm không còn trong danh sách: bỏ họ khỏi những người được tính là đã nhận/đã xem
            var current = result.Items.Select(i => i.UserId).ToHashSet();
            foreach (var gone in x.Receipts.Keys.Where(k => !current.Contains(k)).ToList())
                x.Receipts.Remove(gone);

            foreach (var item in result.Items)
                Upsert(x, item);

            foreach (var m in result.Messages)
            {
                x.MessageReceipts[m.MessageId] = m;
                // Mình đã báo tin này rồi (từ lần trước hoặc thiết bị khác) thì khỏi báo lại
                if (m.DeliveredByUserIds.Contains(myId)) x.DeliveredMessages.Add(m.MessageId);
                if (m.ReadByUserIds.Contains(myId))
                {
                    x.ReadMessages.Add(m.MessageId);
                    x.DeliveredMessages.Add(m.MessageId);
                }
            }
        }
        catch (ApiException)
        {
        }
    }

    // Có người vừa đổi trạng thái (sự kiện SignalR). Trả false nếu đây là người chưa biết tên (mới vào nhóm):
    // cần tải lại từ API receipts để có tên.
    public bool Apply(MessageReceiptChanged change, Guid myId)
    {
        var state = session.State.For(change.ConversationId);
        var x = state.Extras();

        if (change.MessageId is Guid messageId)
        {
            // Nhóm: "người này vừa nhận/xem đúng tin này"
            var entry = GetOrAddMessage(x, messageId, change.Seq ?? 0);
            if (change.IsDelivered == true || change.IsRead == true) AddOnce(entry.DeliveredByUserIds, change.UserId);
            if (change.IsRead == true) AddOnce(entry.ReadByUserIds, change.UserId);

            // Báo từ thiết bị khác của chính mình: ghi nhớ để thiết bị này khỏi báo lại
            if (change.UserId == myId)
            {
                if (change.IsDelivered == true || change.IsRead == true) x.DeliveredMessages.Add(messageId);
                if (change.IsRead == true) x.ReadMessages.Add(messageId);
            }

            return x.Receipts.ContainsKey(change.UserId);
        }

        if (!x.Receipts.TryGetValue(change.UserId, out var existing))
            return false;

        existing.DeliveredSeq = Math.Max(existing.DeliveredSeq, change.DeliveredSeq);
        existing.ReadSeq = Math.Max(existing.ReadSeq, change.ReadSeq);
        return true;
    }

    // Đã LẤY ĐƯỢC dữ liệu các tin này trên thiết bị (hợp đồng: "đã nhận" = thiết bị lấy được tin và xác nhận)
    public async Task MarkDeliveredAsync(Guid conversationId, bool isGroup, Guid myId, IReadOnlyCollection<MessageResponse> messages)
    {
        var state = session.State.For(conversationId);
        var x = state.Extras();

        if (!isGroup)
        {
            var seq = messages.Select(m => m.Seq).DefaultIfEmpty(0).Max();
            if (seq <= x.DeliveredSent) return;

            try
            {
                var result = await api.MarkDeliveredAsync(conversationId, seq);
                x.DeliveredSent = Math.Max(x.DeliveredSent, result.DeliveredSeq);
            }
            catch (ApiException)
            {
                // Không báo được lần này thì lần sau báo lại (mốc chỉ tăng nên báo muộn không sao)
            }
            return;
        }

        // Nhóm: báo từng tin của người khác mà mình chưa báo (tin đã "xem" thì đã tính là "nhận")
        foreach (var m in Pending(messages, myId, x.DeliveredMessages, x.ReadMessages))
        {
            if (await TryConfirmAsync(() => api.MarkDeliveredAsync(conversationId, m.Seq), m.Id, x.DeliveredMessages))
                continue;
            break; // lỗi mạng: dừng, lần sau báo tiếp
        }
    }

    // Người dùng ĐANG MỞ hội thoại và đã đọc các tin này
    public async Task MarkReadAsync(Guid conversationId, bool isGroup, Guid myId, IReadOnlyCollection<MessageResponse> messages)
    {
        var state = session.State.For(conversationId);
        var x = state.Extras();

        if (!isGroup)
        {
            var seq = messages.Select(m => m.Seq).DefaultIfEmpty(0).Max();
            if (seq <= x.ReadSent) return;

            try
            {
                var result = await api.MarkReadAsync(conversationId, seq);
                x.ReadSent = Math.Max(x.ReadSent, result.ReadSeq);
                x.DeliveredSent = Math.Max(x.DeliveredSent, result.DeliveredSeq);
            }
            catch (ApiException)
            {
            }
            return;
        }

        foreach (var m in Pending(messages, myId, x.ReadMessages))
        {
            var ok = await TryConfirmAsync(() => api.MarkReadAsync(conversationId, m.Seq), m.Id, x.ReadMessages);
            if (ok) x.DeliveredMessages.Add(m.Id); // xác nhận "xem" cũng ghi "nhận"
            else break;
        }
    }

    // Chữ dưới tin DO MÌNH GỬI: "Đã gửi", "Đã nhận", "Đã xem" (nhóm: "B, C đã xem"). Tin của người khác thì không có.
    // ignoredUsers: những người mình đã chặn, họ không xem được tin của mình nên không tính là đã nhận/xem.
    public string? StatusFor(Guid conversationId, MessageResponse message, Guid myId, bool isGroup, IReadOnlySet<Guid> ignoredUsers)
    {
        if (message.SenderUserId != myId) return null;

        var x = session.State.For(conversationId).Extras();

        if (isGroup)
        {
            if (!x.MessageReceipts.TryGetValue(message.Id, out var entry)) return "Đã gửi";

            // Chỉ tính thành viên còn trong nhóm (Receipts = danh sách thành viên đang hoạt động), không tính mình và người mình chặn
            bool Counts(Guid id) => id != myId && !ignoredUsers.Contains(id) && x.Receipts.ContainsKey(id);

            var names = entry.ReadByUserIds.Where(Counts)
                .Select(id => string.IsNullOrWhiteSpace(x.Receipts[id].DisplayName) ? "Một thành viên" : x.Receipts[id].DisplayName)
                .ToList();

            if (names.Count > 0)
            {
                return names.Count <= 3
                    ? $"{string.Join(", ", names)} đã xem"
                    : $"{string.Join(", ", names.Take(3))} và {names.Count - 3} người khác đã xem";
            }

            return entry.DeliveredByUserIds.Any(Counts) ? "Đã nhận" : "Đã gửi";
        }

        var others = x.Receipts.Values
            .Where(r => r.UserId != myId && !ignoredUsers.Contains(r.UserId))
            .ToList();

        if (others.Any(r => r.ReadSeq >= message.Seq)) return "Đã xem";
        return others.Any(r => r.DeliveredSeq >= message.Seq) ? "Đã nhận" : "Đã gửi";
    }

    // Các tin của NGƯỜI KHÁC mà mình chưa báo (mới nhất trước, có giới hạn), theo thứ tự seq tăng dần để báo
    private static List<MessageResponse> Pending(IReadOnlyCollection<MessageResponse> messages, Guid myId, params HashSet<Guid>[] done) =>
        messages.Where(m => m.SenderUserId != myId && !done.Any(d => d.Contains(m.Id)))
                .OrderByDescending(m => m.Seq)
                .Take(MaxConfirmPerPass)
                .OrderBy(m => m.Seq)
                .ToList();

    // Báo một tin. 403 (tin đang bị ẩn do chặn...) thì ghi nhớ để khỏi thử lại; lỗi khác (mạng) thì trả false để dừng.
    private static async Task<bool> TryConfirmAsync(Func<Task<ReceiptDto>> call, Guid messageId, HashSet<Guid> done)
    {
        try
        {
            await call();
            done.Add(messageId);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode is 403 or 404)
        {
            done.Add(messageId);
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    private static MessageReceiptItemDto GetOrAddMessage(ConversationExtras x, Guid messageId, long seq)
    {
        if (!x.MessageReceipts.TryGetValue(messageId, out var entry))
            x.MessageReceipts[messageId] = entry = new MessageReceiptItemDto { MessageId = messageId, Seq = seq };
        return entry;
    }

    private static void AddOnce(List<Guid> list, Guid id)
    {
        if (!list.Contains(id)) list.Add(id);
    }

    private static void Upsert(ConversationExtras x, ReceiptItemDto item)
    {
        if (x.Receipts.TryGetValue(item.UserId, out var existing))
        {
            // Mốc chỉ tăng: không bao giờ hạ xuống vì một phản hồi cũ đến muộn
            existing.DisplayName = item.DisplayName;
            existing.DeliveredSeq = Math.Max(existing.DeliveredSeq, item.DeliveredSeq);
            existing.ReadSeq = Math.Max(existing.ReadSeq, item.ReadSeq);
        }
        else
        {
            x.Receipts[item.UserId] = item;
        }
    }
}
