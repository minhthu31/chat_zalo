namespace Chat.Domain.Entities;

public sealed class ConversationNotificationSetting
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public bool IsMuted { get; set; }
}
