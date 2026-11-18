using Chat.Application.DTOs.Attachments;

namespace Chat.Application.Interfaces;

public interface IMessageAttachmentService
{
    Task<SendAttachmentMessageResponse> SendAsync(
        Guid currentUserId,
        Guid conversationId,
        SendAttachmentMessageRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttachmentMessageDto>> GetDetailsAsync(
        Guid currentUserId,
        Guid conversationId,
        IReadOnlyCollection<Guid> messageIds,
        CancellationToken cancellationToken = default);
}
