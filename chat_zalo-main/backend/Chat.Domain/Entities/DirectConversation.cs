namespace Chat.Domain.Entities;

public class DirectConversation
{
    public Guid ConversationId { get; set; }
    public Guid UserLowId { get; set; }
    public Guid UserHighId { get; set; }

    // Navigation property
    public Conversation Conversation { get; set; } = null!;
}

