namespace Chat.Client.Models;

// AttachmentDto của API tệp đính kèm Sprint 3 (mục 3). contentUrl/downloadUrl là đường dẫn TƯƠNG ĐỐI của backend.
public class AttachmentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string ContentUrl { get; set; } = "";
    public string DownloadUrl { get; set; } = "";

    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

// Kết quả GET .../messages/attachment-details: mỗi tin một dòng, tin chữ thì attachments rỗng
public class AttachmentMessageDto
{
    public Guid MessageId { get; set; }
    public List<AttachmentDto> Attachments { get; set; } = new();
}

// Body của POST .../messages/attachments
public class SendAttachmentMessageRequest
{
    public Guid ClientMessageId { get; set; }
    public string Body { get; set; } = "";
    public List<Guid> AttachmentIds { get; set; } = new();
}

// Phản hồi gửi tin kèm tệp: { message, attachments }
public class SendAttachmentMessageResponse
{
    public MessageResponse Message { get; set; } = null!;
    public List<AttachmentDto> Attachments { get; set; } = new();
}

// Một tệp người dùng đã chọn để gửi. Giữ cách mở luồng đọc (không đọc sẵn vào bộ nhớ) và kết quả sau khi tải lên.
public sealed class OutgoingFile
{
    public Guid LocalId { get; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public long Size { get; init; }
    public string ContentType { get; init; } = "";
    public Func<Stream> Open { get; init; } = () => Stream.Null;

    // Có giá trị nghĩa là đã tải lên backend (trạng thái Pending); thử gửi lại tin thì không tải lên lần nữa
    public AttachmentDto? Uploaded { get; set; }
}

// Giới hạn theo hợp đồng Sprint 3. 1 MB = 1.048.576 byte.
public static class AttachmentLimits
{
    public const int MaxFiles = 5;
    public const long MaxFileBytes = 10L * 1024 * 1024;
    public const long MaxTotalBytes = 50L * 1024 * 1024;

    // Giá trị cho thuộc tính accept của ô chọn tệp
    public const string Accept = ".jpg,.jpeg,.png,.webp,.pdf,.docx,.xlsx,.txt";

    // Đuôi tệp -> loại nội dung. Backend kiểm tra lại loại thật từ nội dung, đây chỉ để báo sớm.
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".txt"] = "text/plain"
    };

    // Loại nội dung dùng để gửi; null nghĩa là loại tệp không được hỗ trợ.
    // Dựa vào đuôi tệp vì trình duyệt không phải lúc nào cũng báo đúng loại.
    public static string? ContentTypeFor(string fileName) =>
        Types.GetValueOrDefault(Path.GetExtension(fileName));

    public static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.#} MB" : $"{Math.Max(1, bytes / 1024.0):0.#} KB";
}

// Kiểm tra sớm ở FE trước khi tải lên: đủ số lượng, loại, dung lượng từng tệp và tổng dung lượng
public static class AttachmentRules
{
    // Trả về câu báo lỗi nếu KHÔNG được thêm tệp này vào danh sách hiện có, null nếu hợp lệ
    public static string? CheckCanAdd(IReadOnlyList<OutgoingFile> current, string fileName, long size)
    {
        if (current.Count >= AttachmentLimits.MaxFiles)
            return $"Mỗi tin tối đa {AttachmentLimits.MaxFiles} tệp.";

        if (AttachmentLimits.ContentTypeFor(fileName) is null)
            return $"\"{fileName}\": loại tệp không được hỗ trợ (chỉ nhận JPG, PNG, WebP, PDF, DOCX, XLSX, TXT).";

        if (size <= 0)
            return $"\"{fileName}\" là tệp rỗng.";

        if (size > AttachmentLimits.MaxFileBytes)
            return $"\"{fileName}\" quá lớn ({AttachmentLimits.FormatSize(size)}). Mỗi tệp tối đa 10 MB.";

        if (current.Sum(f => f.Size) + size > AttachmentLimits.MaxTotalBytes)
            return $"Tổng dung lượng mỗi tin tối đa 50 MB. Không thêm được \"{fileName}\".";

        return null;
    }
}
