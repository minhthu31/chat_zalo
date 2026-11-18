namespace Chat.Domain.Entities;

public sealed class StoredFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UploadedByUserId { get; set; }
    public Guid? ConversationId { get; set; }
    public string UsageType { get; set; } = "Attachment";
    public string Status { get; set; } = "Pending";
    public string StorageKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
