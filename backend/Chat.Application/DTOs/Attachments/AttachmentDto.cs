namespace Chat.Application.DTOs.Attachments;

public sealed class AttachmentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ContentUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
}
