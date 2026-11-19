namespace Chat.Application.DTOs.Receipts;

public sealed class MessageReceiptChangedDto
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public long DeliveredSeq { get; set; }
    public long ReadSeq { get; set; }
}
