namespace Chat.Application.DTOs.Receipts;

public sealed class ConversationReceiptItemDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public long DeliveredSeq { get; set; }
    public long ReadSeq { get; set; }
}
