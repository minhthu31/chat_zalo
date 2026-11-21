namespace Chat.Application.DTOs.Receipts;

public sealed class MessageReceiptItemDto
{
    public Guid MessageId { get; set; }
    public long Seq { get; set; }
    public Guid SenderUserId { get; set; }
    public List<Guid> DeliveredByUserIds { get; set; } = [];
    public List<Guid> ReadByUserIds { get; set; } = [];
}
