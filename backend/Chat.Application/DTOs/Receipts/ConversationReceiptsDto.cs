namespace Chat.Application.DTOs.Receipts;

public sealed class ConversationReceiptsDto
{
    public Guid ConversationId { get; set; }
    public List<ConversationReceiptItemDto> Items { get; set; } = [];
}
