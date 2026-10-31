namespace Chat.Domain.Entities;

public class Friendship
{
    public Guid UserLowId { get; set; }
    public Guid UserHighId { get; set; }
    public Guid RequesterUserId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}

