using Chat.Client.Models;
namespace Chat.Client.Services;

public interface IChatConversations
{
    Task<List<ConversationDto>> GetAsync();
}

public sealed class ConversationsApiClient(ApiRequestSender api) : IChatConversations
{
    public Task<List<ConversationDto>> GetAsync() =>
        api.SendAsync<List<ConversationDto>>(HttpMethod.Get, "api/conversations");
    public Task<ConversationDto> CreateDirectAsync(Guid targetUserId) =>
        api.SendAsync<ConversationDto>(HttpMethod.Post, "api/conversations/direct", new { targetUserId });
}
