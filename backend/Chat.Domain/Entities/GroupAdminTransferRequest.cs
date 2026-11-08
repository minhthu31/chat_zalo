namespace Chat.Domain.Entities;

public class GroupAdminTransferRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }

    public GroupConversation GroupConversation { get; set; } = null!;
}
