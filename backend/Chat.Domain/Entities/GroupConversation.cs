namespace Chat.Domain.Entities;

public class GroupConversation
{
    public Guid ConversationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset? DeletedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public ICollection<GroupAdminTransferRequest> AdminTransferRequests { get; set; } = new List<GroupAdminTransferRequest>();
}
