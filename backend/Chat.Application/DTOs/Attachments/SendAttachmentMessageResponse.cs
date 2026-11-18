namespace Chat.Application.DTOs.Attachments;

using Chat.Application.DTOs.Messages;

public sealed class SendAttachmentMessageResponse
{
    public MessageResponse Message { get; set; } = null!;
    public List<AttachmentDto> Attachments { get; set; } = [];
}
