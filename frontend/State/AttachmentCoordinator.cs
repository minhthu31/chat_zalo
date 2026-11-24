using Chat.Client.Models;
using Chat.Client.Services;

namespace Chat.Client.State;

// Báo lỗi gửi tệp với câu đã dịch sẵn cho người dùng (kèm tên tệp bị lỗi). Chỉ dùng cho lỗi dữ liệu / mạng,
// còn lỗi 401/403 vẫn để ApiException nguyên bản đi lên để trang chat xử lý chung.
public sealed class AttachmentSendException(string message) : Exception(message);

// Phần tệp đính kèm của trò chuyện, tách riêng khỏi ChatSession để không đụng vào logic đồng bộ tin nhắn sẵn có:
//  - gửi tin kèm tệp (tải lên từng tệp, rồi gửi tin cùng mã các tệp);
//  - bổ sung thông tin tệp cho các tin vừa tải về (GET attachment-details).
public sealed class AttachmentCoordinator(ChatSession session, IAttachmentApiClient api)
{
    private const int DetailsBatch = 100; // hợp đồng: tối đa 100 mã tin mỗi lần hỏi

    // Hỏi backend tệp của những tin chưa hỏi. Lỗi (mất mạng, mất quyền) thì bỏ qua: tin chữ vẫn hiện, lần sau hỏi lại.
    public async Task EnrichAsync(Guid conversationId)
    {
        var state = session.State.For(conversationId);
        var x = state.Extras();
        var pending = state.Messages.Select(m => m.Id).Where(id => !x.AttachmentsChecked.Contains(id)).ToList();

        foreach (var batch in pending.Chunk(DetailsBatch))
        {
            try
            {
                foreach (var item in await api.GetDetailsAsync(conversationId, batch))
                {
                    x.Attachments[item.MessageId] = item.Attachments;
                    x.AttachmentsChecked.Add(item.MessageId);
                }
            }
            catch (ApiException)
            {
                return;
            }
        }
    }

    // Gửi tin kèm các tệp đã chọn (x.Files) và chữ trong ô nhập (state.Draft, được để trống).
    public async Task SendWithFilesAsync(Guid conversationId, Action? onProgress = null)
    {
        var state = session.State.For(conversationId);
        var x = state.Extras();
        if (state.Sending || !state.Initialized || x.Files.Count == 0) return;
        if (state.Draft.Length > 4000) throw new AttachmentSendException("Tin nhắn tối đa 4.000 ký tự.");

        state.Sending = true;
        var current = "";

        try
        {
            // 1. Tải lên từng tệp (mỗi lần một tệp). Tệp đã lên rồi (gửi lại sau lỗi) thì không tải lại.
            var total = x.Files.Count;
            for (var i = 0; i < x.Files.Count; i++)
            {
                var file = x.Files[i];
                if (file.Uploaded is not null) continue;

                current = file.Name;
                x.UploadStatus = $"Đang tải lên {i + 1}/{total}: {file.Name}";
                onProgress?.Invoke();

                await using var stream = file.Open();
                file.Uploaded = await api.UploadAsync(conversationId, stream, file.Name, file.ContentType);
            }

            // 2. Gửi tin kèm mã các tệp. Gửi lại sau lỗi mạng thì dùng lại cùng ClientMessageId để không tạo tin thứ hai.
            current = "";
            x.UploadStatus = "Đang gửi tin...";
            onProgress?.Invoke();

            x.PendingFileClientId ??= Guid.NewGuid();
            var request = new SendAttachmentMessageRequest
            {
                ClientMessageId = x.PendingFileClientId.Value,
                Body = state.Draft,
                AttachmentIds = x.Files.Select(f => f.Uploaded!.Id).ToList()
            };

            var response = await api.SendMessageAsync(conversationId, request);

            state.Merge(conversationId, [response.Message]);
            x.Attachments[response.Message.Id] = response.Attachments;
            x.AttachmentsChecked.Add(response.Message.Id);

            x.Files.Clear();
            state.Draft = "";
            x.PendingFileClientId = null;
        }
        catch (ApiException ex) when (ex.StatusCode is not (401 or 403))
        {
            // Lỗi do dữ liệu (4xx): bỏ mã chống trùng để lần sau tạo mã mới; tệp không còn dùng được thì tải lên lại.
            // Lỗi mạng (mã 0) hoặc lỗi máy chủ: giữ nguyên để người dùng bấm gửi lại.
            if (ex.StatusCode is >= 400 and < 500)
            {
                x.PendingFileClientId = null;
                if (ex.Code is "ATTACHMENT_NOT_FOUND" or "ATTACHMENT_ALREADY_USED")
                    foreach (var f in x.Files) f.Uploaded = null;
            }

            var text = AttachmentErrors.ToMessage(ex.Code);
            throw new AttachmentSendException(current == "" ? text : $"\"{current}\": {text}");
        }
        finally
        {
            state.Sending = false;
            x.UploadStatus = null;
            onProgress?.Invoke();
        }

        await session.SyncAsync(conversationId);
    }
}
