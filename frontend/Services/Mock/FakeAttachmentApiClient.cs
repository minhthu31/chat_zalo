using Chat.Client.Models;

namespace Chat.Client.Services.Mock;

public class FakeAttachmentApiClient : IAttachmentApiClient
{
    private static readonly Guid Me = Guid.Parse("550e8400-e29b-41d4-a716-446655440001");

    private readonly Dictionary<Guid, AttachmentDto> _pending = new();
    private readonly Dictionary<Guid, List<AttachmentDto>> _byMessage = new();
    private long _seq;

    public async Task<AttachmentDto> UploadAsync(Guid conversationId, Stream content, string fileName, string contentType)
    {
        await Task.Delay(300);

        if (AttachmentLimits.ContentTypeFor(fileName) is null)
            throw new ApiException(415, "UNSUPPORTED_FILE_TYPE", "Unsupported file type");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);
        if (buffer.Length > AttachmentLimits.MaxFileBytes)
            throw new ApiException(413, "FILE_TOO_LARGE", "File too large");

        var id = Guid.NewGuid();
        var dto = new AttachmentDto
        {
            Id = id,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = buffer.Length,
            ContentUrl = $"/api/attachments/{id}/content",
            DownloadUrl = $"/api/attachments/{id}/download"
        };
        _pending[id] = dto;
        return dto;
    }

    public async Task<SendAttachmentMessageResponse> SendMessageAsync(Guid conversationId, SendAttachmentMessageRequest request)
    {
        await Task.Delay(300);

        if (request.AttachmentIds.Count is < 1 or > AttachmentLimits.MaxFiles
            || request.AttachmentIds.Distinct().Count() != request.AttachmentIds.Count)
            throw new ApiException(400, "INVALID_ATTACHMENT_COUNT", "Invalid attachment count");

        var files = new List<AttachmentDto>();
        foreach (var id in request.AttachmentIds)
        {
            if (!_pending.Remove(id, out var file))
                throw new ApiException(404, "ATTACHMENT_NOT_FOUND", "Attachment not found");
            files.Add(file);
        }

        var message = new MessageResponse(Guid.NewGuid(), conversationId, Me, ++_seq, request.Body, DateTimeOffset.UtcNow);
        _byMessage[message.Id] = files;
        return new SendAttachmentMessageResponse { Message = message, Attachments = files };
    }

    public async Task<List<AttachmentMessageDto>> GetDetailsAsync(Guid conversationId, IReadOnlyCollection<Guid> messageIds)
    {
        await Task.Delay(150);
        return messageIds.Select(id => new AttachmentMessageDto
        {
            MessageId = id,
            Attachments = _byMessage.GetValueOrDefault(id) ?? new()
        }).ToList();
    }
}
