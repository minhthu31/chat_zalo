using Chat.Client.Models;

namespace Chat.Client.Services;

// Bản thật của IConversationSettingsApiClient
public class ConversationSettingsApiClient : IConversationSettingsApiClient
{
    private readonly ApiRequestSender _api;

    public ConversationSettingsApiClient(ApiRequestSender api)
    {
        _api = api;
    }

    public Task<NotificationSettingsDto> GetNotificationSettingsAsync(Guid conversationId) =>
        _api.SendAsync<NotificationSettingsDto>(HttpMethod.Get, $"api/conversations/{conversationId}/notification-settings");

    public Task<NotificationSettingsDto> SetMutedAsync(Guid conversationId, bool isMuted) =>
        _api.SendAsync<NotificationSettingsDto>(HttpMethod.Put, $"api/conversations/{conversationId}/notification-settings",
            new SetNotificationSettingsRequest { IsMuted = isMuted });
}
