namespace Chat.Client.Models;

// Mốc "đã nhận / đã xem" của một người trong một hội thoại (hợp đồng Sprint 3, mục 5).
// Mốc là số thứ tự tin (Seq) và CHỈ TĂNG: deliveredSeq = đã nhận tới tin này, readSeq = đã xem tới tin này.
public class ReceiptDto
{
    public Guid UserId { get; set; }
    public long DeliveredSeq { get; set; }
    public long ReadSeq { get; set; }

    // Chỉ có ở chat NHÓM: lần xác nhận này là cho đúng một tin (hợp đồng CHAT-28)
    public Guid? MessageId { get; set; }
    public long? Seq { get; set; }
    public bool? IsDelivered { get; set; }
    public bool? IsRead { get; set; }
}

public class ReceiptItemDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public long DeliveredSeq { get; set; }
    public long ReadSeq { get; set; }
}

// Kết quả GET /api/conversations/{id}/receipts: mốc của các thành viên đang hoạt động
public class ConversationReceiptsDto
{
    public Guid ConversationId { get; set; }
    public List<ReceiptItemDto> Items { get; set; } = new();
    // Chỉ có ở chat NHÓM: trạng thái TỪNG TIN (ai đã nhận, ai đã xem)
    public List<MessageReceiptItemDto> Messages { get; set; } = new();
}

// Trạng thái của một tin trong nhóm: mã người đã nhận / đã xem (tên lấy từ Items)
public class MessageReceiptItemDto
{
    public Guid MessageId { get; set; }
    public long Seq { get; set; }
    public Guid SenderUserId { get; set; }
    public List<Guid> DeliveredByUserIds { get; set; } = new();
    public List<Guid> ReadByUserIds { get; set; } = new();
}

// Body của PUT .../receipts/delivered và .../receipts/read
public class MarkReceiptRequest
{
    public long Seq { get; set; }
}

// Sự kiện SignalR MessageReceiptChanged: ai đó vừa đổi mốc. Không có tên, FE ghép tên từ API receipts.
// Chat nhóm: có thêm MessageId/Seq/IsDelivered/IsRead = "người này vừa nhận/xem đúng tin này".
public sealed record MessageReceiptChanged(
    Guid ConversationId,
    Guid UserId,
    long DeliveredSeq,
    long ReadSeq,
    Guid? MessageId = null,
    long? Seq = null,
    bool? IsDelivered = null,
    bool? IsRead = null);
