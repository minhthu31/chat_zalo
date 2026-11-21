namespace Chat.Application.DTOs.Receipts;

public sealed class ReceiptDto
{
    public Guid UserId { get; set; }
    public Guid? MessageId { get; set; }
    public long? Seq { get; set; }
    public bool? IsDelivered { get; set; }
    public bool? IsRead { get; set; }
    public long DeliveredSeq { get; set; }
    public long ReadSeq { get; set; }
}
