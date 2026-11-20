using System.Text.Json.Serialization;

namespace Chat.Application.DTOs.Notifications;

public sealed class UpdateNotificationSettingsRequest
{
    [JsonRequired]
    public bool IsMuted { get; set; }
}
