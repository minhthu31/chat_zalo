namespace Chat.Application.DTOs.Groups;

public class AdminTransferDto
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; }
}
