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
//    "Đã nhận" gửi khi tải được tin về; "Đã xem" CHỈ gửi cho tin đang nằm trong vùng nhìn thấy khi trang đang hoạt động.
public sealed class ReceiptCoordinator(ChatSession session, IMessageReceiptApiClient api)
{
    private const int PageIds = 100;          // hợp đồng: GET receipts nhận tối đa 100 mã tin
    private const int MaxConfirmPerPass = 30; // mỗi lượt chỉ báo tối đa chừng này tin (tránh bắn quá nhiều yêu cầu một lúc)
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(20); // tin vừa bị 403/404: chờ chừng này rồi mới thử lại

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
            Absorb(x, result, myId);
        }
        catch (ApiException)
        {
        }
    }

    // Nhóm: hỏi trạng thái của những tin MỚI VỀ máy mà chưa hỏi (vd trang tin cũ vừa cuộn lên, tin hiện lại sau khi bỏ chặn).
    // Mỗi lần chỉ gửi mã của các tin đó (tối đa 100 mã một yêu cầu), không hỏi lại tin đã biết.
    public async Task LoadMissingAsync(Guid conversationId, Guid myId)
    {
        var state = session.State.For(conversationId);
        var x = state.Extras();
        if (x.LoadingReceipts) return;

        ForgetHidden(x, state.Messages);

        var now = DateTimeOffset.UtcNow;
        var missing = state.Messages
            .Where(m => !x.ReceiptsChecked.Contains(m.Id)
                        && !(x.ReceiptsRetryAfter.TryGetValue(m.Id, out var after) && after > now))
            .OrderByDescending(m => m.Seq)
            .Select(m => m.Id)
            .ToList();
        if (missing.Count == 0) return;

        x.LoadingReceipts = true;
        try
        {
            foreach (var batch in missing.Chunk(PageIds))
            {
                var result = await api.GetAsync(conversationId, batch);
                Absorb(x, result, myId);

                // Tin không được trả về (đang bị ẩn do chặn...): chưa coi là đã biết, thử lại sau một lúc
                var returned = result.Messages.Select(m => m.MessageId).ToHashSet();
                foreach (var id in batch.Where(id => !returned.Contains(id)))
                    x.ReceiptsRetryAfter[id] = now + RetryDelay;
            }
        }
        catch (ApiException)
        {
            // Lỗi mạng: lần đồng bộ sau sẽ hỏi lại
        }
        finally
        {
            x.LoadingReceipts = false;
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
        ForgetHidden(x, state.Messages);
        foreach (var m in Pending(x, messages, myId, x.DeliveredMessages, x.ReadMessages))
        {
            var result = await TryConfirmAsync(x, m.Id, () => api.MarkDeliveredAsync(conversationId, m.Seq));
            if (result == Confirm.Done) x.DeliveredMessages.Add(m.Id);
            else if (result == Confirm.Failed) break; // lỗi mạng: dừng, lần sau báo tiếp
        }
    }

    // Người dùng đã THẤY các tin này. Chat cá nhân: báo mốc tới tin lớn nhất trong danh sách (trang gọi khi đang mở và đang nhìn).
    // Chat nhóm: messages PHẢI là các tin đang nằm trong vùng nhìn thấy (trang đang hoạt động); chỉ những tin đó được báo "đã xem".
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

        foreach (var m in Pending(x, messages, myId, x.ReadMessages))
        {
            var result = await TryConfirmAsync(x, m.Id, () => api.MarkReadAsync(conversationId, m.Seq));
            if (result == Confirm.Done)
            {
                x.ReadMessages.Add(m.Id);
                x.DeliveredMessages.Add(m.Id); // xác nhận "xem" cũng ghi "nhận"
            }
            else if (result == Confirm.Failed) break;
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

    // Tin KHÔNG còn trong danh sách đang hiển thị (bị ẩn do chặn...) thì quên thời gian chờ thử lại và việc "đã hỏi receipts" của nó.
    // Nhờ vậy khi tin hiện lại sau khi bỏ chặn, FE hỏi lại trạng thái và gửi xác nhận lại ngay, không phải chờ hết thời gian cũ.
    private static void ForgetHidden(ConversationExtras x, IReadOnlyList<MessageResponse> loaded)
    {
        if (x.ReceiptsRetryAfter.Count == 0 && x.ReceiptsChecked.Count == 0) return;

        var present = loaded.Select(m => m.Id).ToHashSet();
        foreach (var id in x.ReceiptsRetryAfter.Keys.Where(k => !present.Contains(k)).ToList())
            x.ReceiptsRetryAfter.Remove(id);
        x.ReceiptsChecked.RemoveWhere(id => !present.Contains(id));
    }

    private enum Confirm { Done, Skipped, Failed }

    // Các tin của NGƯỜI KHÁC mà mình chưa báo (mới nhất trước, có giới hạn), theo thứ tự seq tăng dần để báo.
    // Bỏ qua tin đang được báo dở và tin vừa bị 403/404 (đang chờ thử lại).
    private static List<MessageResponse> Pending(ConversationExtras x, IReadOnlyCollection<MessageResponse> messages, Guid myId, params HashSet<Guid>[] done)
    {
        var now = DateTimeOffset.UtcNow;
        return messages
            .Where(m => m.SenderUserId != myId
                        && !done.Any(d => d.Contains(m.Id))
                        && !x.ReceiptsInFlight.Contains(m.Id)
                        && !(x.ReceiptsRetryAfter.TryGetValue(m.Id, out var after) && after > now))
            .OrderByDescending(m => m.Seq)
            .Take(MaxConfirmPerPass)
            .OrderBy(m => m.Seq)
            .ToList();
    }

    // Báo một tin.
    //  - Thành công: Done.
    //  - 403/404 (tin đang bị ẩn do chặn, hoặc không còn): KHÔNG coi là đã xác nhận. Chờ một lúc rồi thử lại; khi tin hiện lại
    //    và đủ điều kiện thì lần sau sẽ gửi được. Trả Skipped để lượt này sang tin kế tiếp.
    //  - Lỗi khác (mạng, máy chủ): Failed để dừng lượt này.
    private static async Task<Confirm> TryConfirmAsync(ConversationExtras x, Guid messageId, Func<Task<ReceiptDto>> call)
    {
        if (!x.ReceiptsInFlight.Add(messageId)) return Confirm.Skipped;

        try
        {
            await call();
            x.ReceiptsRetryAfter.Remove(messageId);
            return Confirm.Done;
        }
        catch (ApiException ex) when (ex.StatusCode is 403 or 404)
        {
            x.ReceiptsRetryAfter[messageId] = DateTimeOffset.UtcNow + RetryDelay;
            return Confirm.Skipped;
        }
        catch (ApiException)
        {
            return Confirm.Failed;
        }
        finally
        {
            x.ReceiptsInFlight.Remove(messageId);
        }
    }

    // Gộp kết quả GET receipts vào trạng thái (cả mốc từng người lẫn trạng thái từng tin)
    private static void Absorb(ConversationExtras x, ConversationReceiptsDto result, Guid myId)
    {
        // Người đã rời nhóm không còn trong danh sách: bỏ họ khỏi những người được tính là đã nhận/đã xem
        var current = result.Items.Select(i => i.UserId).ToHashSet();
        foreach (var gone in x.Receipts.Keys.Where(k => !current.Contains(k)).ToList())
            x.Receipts.Remove(gone);

        foreach (var item in result.Items)
            Upsert(x, item);

        foreach (var m in result.Messages)
        {
            // Gộp (hợp) với những gì đã nhận qua sự kiện, để không mất thông tin đến trước phản hồi này
            var entry = GetOrAddMessage(x, m.MessageId, m.Seq);
            entry.Seq = m.Seq;
            entry.SenderUserId = m.SenderUserId;
            foreach (var id in m.DeliveredByUserIds) AddOnce(entry.DeliveredByUserIds, id);
            foreach (var id in m.ReadByUserIds) AddOnce(entry.ReadByUserIds, id);

            x.ReceiptsChecked.Add(m.MessageId);
            x.ReceiptsRetryAfter.Remove(m.MessageId);

            // Mình đã báo tin này rồi (từ lần trước hoặc thiết bị khác) thì khỏi báo lại
            if (m.DeliveredByUserIds.Contains(myId)) x.DeliveredMessages.Add(m.MessageId);
            if (m.ReadByUserIds.Contains(myId))
            {
                x.ReadMessages.Add(m.MessageId);
                x.DeliveredMessages.Add(m.MessageId);
            }
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
