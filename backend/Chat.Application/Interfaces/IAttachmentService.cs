using Chat.Application.DTOs.Attachments;
using Chat.Application.DTOs.Files;

namespace Chat.Application.Interfaces;

public interface IAttachmentService
{
    Task<AttachmentDto> UploadAsync(
        Guid currentUserId,
        Guid conversationId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<FileContentResult> OpenContentAsync(
        Guid currentUserId,
        Guid attachmentId,
        CancellationToken cancellationToken = default);
}
