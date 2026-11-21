namespace Chat.Domain.Entities;

/// <summary>Receipt for exactly one message and one recipient; never inferred from a group cursor.</summary>
public sealed class MessageUserReceipt
{
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
