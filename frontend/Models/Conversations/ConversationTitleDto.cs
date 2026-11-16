namespace Chat.Client.Models;

public sealed record ConversationTitleDto(Guid ConversationId, string? Username, string? DisplayName, Guid UserId)
{
    public string Title => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
        : !string.IsNullOrWhiteSpace(Username) ? Username : "Trò chuyện cá nhân";
}
