namespace Chat.Domain.Entities;

public sealed class MessageAttachment
{
    public Guid MessageId { get; set; }
    public Guid StoredFileId { get; set; }

    public Message Message { get; set; } = null!;
    public StoredFile StoredFile { get; set; } = null!;
}
