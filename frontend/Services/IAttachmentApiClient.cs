using Chat.Client.Models;

namespace Chat.Client.Services;

// Tệp đính kèm trong chat (hợp đồng Sprint 3, mục 3). Một lần tải lên một tệp.
public interface IAttachmentApiClient
{
    // POST /api/conversations/{id}/attachments (multipart, trường "file") -> 201
    Task<AttachmentDto> UploadAsync(Guid conversationId, Stream content, string fileName, string contentType);

    // POST /api/conversations/{id}/messages/attachments -> 201 (tạo mới) hoặc 200 (gửi lại cùng ClientMessageId)
    Task<SendAttachmentMessageResponse> SendMessageAsync(Guid conversationId, SendAttachmentMessageRequest request);

    // GET /api/conversations/{id}/messages/attachment-details?messageIds=a,b (tối đa 100 mã)
    Task<List<AttachmentMessageDto>> GetDetailsAsync(Guid conversationId, IReadOnlyCollection<Guid> messageIds);
}
