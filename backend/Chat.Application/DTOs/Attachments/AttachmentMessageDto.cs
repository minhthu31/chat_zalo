namespace Chat.Application.DTOs.Attachments;

public sealed class AttachmentMessageDto
{
    public Guid MessageId { get; set; }
    public List<AttachmentDto> Attachments { get; set; } = [];
}
