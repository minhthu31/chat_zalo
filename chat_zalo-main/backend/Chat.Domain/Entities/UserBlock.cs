namespace Chat.Domain.Entities;

public class UserBlock
{
    public Guid BlockerUserId { get; set; }
    public Guid BlockedUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

