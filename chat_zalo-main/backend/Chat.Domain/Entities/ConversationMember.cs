namespace Chat.Domain.Entities;

public class ConversationMember
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "Member";
    public long JoinedSeq { get; set; } = 0;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeftAt { get; set; }
    public long LastReadSeq { get; set; } = 0;
    public long LastDeliveredSeq { get; set; } = 0;

    // Navigation property
    public Conversation Conversation { get; set; } = null!;
}

