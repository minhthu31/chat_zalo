using Chat.Client.Models;

namespace Chat.Client.Services;

// Cài đặt thông báo theo từng hội thoại, lưu riêng cho mỗi tài khoản (dùng chung mọi thiết bị)
public interface IConversationSettingsApiClient
{
    Task<NotificationSettingsDto> GetNotificationSettingsAsync(Guid conversationId);             // GET /api/conversations/{id}/notification-settings
    Task<NotificationSettingsDto> SetMutedAsync(Guid conversationId, bool isMuted);               // PUT cùng địa chỉ, idempotent
}
