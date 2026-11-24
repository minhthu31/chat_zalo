using Chat.Client.Models;

namespace Chat.Client.Services;

// Bản thật của IAttachmentApiClient
public class AttachmentApiClient : IAttachmentApiClient
{
    private readonly ApiRequestSender _api;

    public AttachmentApiClient(ApiRequestSender api)
    {
        _api = api;
    }

    public Task<AttachmentDto> UploadAsync(Guid conversationId, Stream content, string fileName, string contentType) =>
        _api.SendFileAsync<AttachmentDto>(HttpMethod.Post, $"api/conversations/{conversationId}/attachments", content, fileName, contentType);

    public Task<SendAttachmentMessageResponse> SendMessageAsync(Guid conversationId, SendAttachmentMessageRequest request) =>
        _api.SendAsync<SendAttachmentMessageResponse>(HttpMethod.Post, $"api/conversations/{conversationId}/messages/attachments", request);

    // Hợp đồng: danh sách Guid ngăn cách bằng dấu phẩy, tối đa 100
    public Task<List<AttachmentMessageDto>> GetDetailsAsync(Guid conversationId, IReadOnlyCollection<Guid> messageIds) =>
        _api.SendAsync<List<AttachmentMessageDto>>(HttpMethod.Get,
            $"api/conversations/{conversationId}/messages/attachment-details?messageIds={string.Join(',', messageIds)}");
}
