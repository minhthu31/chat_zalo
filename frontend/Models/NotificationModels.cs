namespace Chat.Client.Models;

// Body và kết quả của GET/PUT /api/conversations/{id}/notification-settings (hợp đồng Sprint 3, mục 4)
public class NotificationSettingsDto
{
    public bool IsMuted { get; set; }
}

public class SetNotificationSettingsRequest
{
    public bool IsMuted { get; set; }
}

// Một thông báo nổi (banner) báo có tin mới ở một hội thoại
public sealed record ToastItem(Guid Id, Guid ConversationId, string Title, string Text);
