using System.Runtime.CompilerServices;
using Chat.Client.Models;

namespace Chat.Client.State;

// Trạng thái Sprint 3 của MỖI hội thoại (tệp đính kèm, mốc đã nhận/đã xem), tách khỏi ConversationState.
// Lý do tách: dự án test của frontend chỉ biên dịch một danh sách cố định các file (có ChatState.cs), nên ChatState.cs
// không được phụ thuộc vào các kiểu dữ liệu mới. Gắn kèm bằng extension nên khi ConversationState bị tạo mới
// (đăng xuất, đổi phiên) thì phần này cũng tự mất theo.
public sealed class ConversationExtras
{
    // ---- Tệp đính kèm ----
    // Tệp của các tin đã tải: mã tin -> danh sách tệp (danh sách rỗng nghĩa là tin chỉ có chữ)
    public Dictionary<Guid, IReadOnlyList<AttachmentDto>> Attachments { get; } = [];
    // Các tin đã hỏi backend về tệp, để không hỏi lại mỗi lần
    public HashSet<Guid> AttachmentsChecked { get; } = [];
    // Tệp người dùng đã chọn, chờ gửi cùng tin tiếp theo
    public List<OutgoingFile> Files { get; } = [];
    // Mã chống trùng của tin kèm tệp đang gửi dở: gửi lại cùng mã thì backend không tạo tin thứ hai
    public Guid? PendingFileClientId { get; set; }
    // Dòng trạng thái khi đang tải lên ("Đang tải lên 2/3: ten.pdf")
    public string? UploadStatus { get; set; }

    // ---- Đã nhận / đã xem ----
    // Mốc của từng thành viên (mã người dùng -> mốc đã nhận, đã xem)
    public Dictionary<Guid, ReceiptItemDto> Receipts { get; } = [];
    // Mốc MÌNH đã báo lên backend, để không gửi lại cùng một mốc nhiều lần
    public long DeliveredSent { get; set; }
    public long ReadSent { get; set; }

    // Chat NHÓM xác nhận theo từng tin: trạng thái từng tin (mã tin -> ai đã nhận / đã xem)
    public Dictionary<Guid, MessageReceiptItemDto> MessageReceipts { get; } = [];
    // Các tin MÌNH đã báo "đã nhận" / "đã xem" (nhóm), để không báo lại
    public HashSet<Guid> DeliveredMessages { get; } = [];
    public HashSet<Guid> ReadMessages { get; } = [];
    // Các tin đã tải trạng thái người nhận/người xem từ GET receipts (để mỗi lần chỉ hỏi những tin MỚI về, vd trang tin cũ vừa cuộn lên)
    public HashSet<Guid> ReceiptsChecked { get; } = [];
    // Tin đang được gửi xác nhận (tránh gửi trùng khi vừa cuộn vừa tải) và tin vừa bị 403/404 (chờ một lúc rồi mới thử lại,
    // KHÔNG coi là đã xác nhận: khi tin đủ điều kiện hiện lại thì vẫn gửi được)
    public HashSet<Guid> DeliveredInFlight { get; } = [];
    public HashSet<Guid> ReadInFlight { get; } = [];
    public Dictionary<Guid, DateTimeOffset> ReceiptsRetryAfter { get; } = [];
    public bool LoadingReceipts { get; set; }
}

public static class ConversationStateExtensions
{
    private static readonly ConditionalWeakTable<ConversationState, ConversationExtras> Table = new();

    // Phần trạng thái Sprint 3 của hội thoại này (tạo khi dùng lần đầu)
    public static ConversationExtras Extras(this ConversationState state) => Table.GetValue(state, _ => new ConversationExtras());
}
