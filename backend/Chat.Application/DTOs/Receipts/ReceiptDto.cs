namespace Chat.Application.DTOs.Receipts;

public sealed class ReceiptDto
{
    public Guid UserId { get; set; }
    public long DeliveredSeq { get; set; }
    public long ReadSeq { get; set; }
}
