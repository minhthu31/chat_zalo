namespace Chat.Application.DTOs.Attachments;

public sealed class SendAttachmentMessageRequest
{
    public Guid ClientMessageId { get; set; }
    public string Body { get; set; } = string.Empty;
    public List<Guid> AttachmentIds { get; set; } = [];
}
