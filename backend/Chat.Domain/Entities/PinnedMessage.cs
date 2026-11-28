namespace Chat.Domain.Entities;

public sealed class PinnedMessage
{
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public DateTimeOffset PinnedAt { get; set; }
}
