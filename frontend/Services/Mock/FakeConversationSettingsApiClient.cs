using Chat.Client.Models;

namespace Chat.Client.Services.Mock;

public class FakeConversationSettingsApiClient : IConversationSettingsApiClient
{
    private readonly Dictionary<Guid, bool> _muted = new();

    public async Task<NotificationSettingsDto> GetNotificationSettingsAsync(Guid conversationId)
    {
        await Task.Delay(150);
        return new NotificationSettingsDto { IsMuted = _muted.GetValueOrDefault(conversationId) };
    }

    public async Task<NotificationSettingsDto> SetMutedAsync(Guid conversationId, bool isMuted)
    {
        await Task.Delay(200);
        _muted[conversationId] = isMuted;
        return new NotificationSettingsDto { IsMuted = isMuted };
    }
}
