using Chat.Client.Models.Conversations;

namespace Chat.Client.Services;

public sealed class ConversationsApiClient(ChatApiClient api)
{
    public Task<IReadOnlyList<ConversationDto>> GetConversationsAsync(CancellationToken ct = default) =>
        api.GetConversationsAsync(ct);
}
