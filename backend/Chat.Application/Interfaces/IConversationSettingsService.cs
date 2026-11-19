using Chat.Application.DTOs.Notifications;

namespace Chat.Application.Interfaces;

public interface IConversationSettingsService
{
    Task<NotificationSettingsDto> GetAsync(Guid currentUserId, Guid conversationId, CancellationToken cancellationToken = default);
    Task<NotificationSettingsDto> UpdateAsync(
        Guid currentUserId,
        Guid conversationId,
        UpdateNotificationSettingsRequest request,
        CancellationToken cancellationToken = default);
}
