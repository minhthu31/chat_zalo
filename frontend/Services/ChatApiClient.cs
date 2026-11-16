using Chat.Client.Models;

namespace Chat.Client.Services;

public sealed class ChatApiClient(ApiRequestSender api)
{
    public Task<List<ConversationDto>> ConversationsAsync() =>
        api.SendAsync<List<ConversationDto>>(HttpMethod.Get, "api/conversations");
    public Task<ConversationDto> CreateDirectAsync(Guid targetUserId) =>
        api.SendAsync<ConversationDto>(HttpMethod.Post, "api/conversations/direct", new { targetUserId });
    public Task<List<MessageResponse>> GetNewAsync(Guid conversationId, long afterSeq) =>
        api.SendAsync<List<MessageResponse>>(HttpMethod.Get, $"api/conversations/{conversationId}/messages?afterSeq={afterSeq}&limit=100");
    public Task<List<MessageResponse>> GetLatestAsync(Guid conversationId) =>
        api.SendAsync<List<MessageResponse>>(HttpMethod.Get, $"api/conversations/{conversationId}/messages?limit=20");
    public Task<List<MessageResponse>> GetOlderAsync(Guid conversationId, long beforeSeq) =>
        api.SendAsync<List<MessageResponse>>(HttpMethod.Get, $"api/conversations/{conversationId}/messages?beforeSeq={beforeSeq}&limit=20");
    public Task<MessageResponse> SendAsync(Guid conversationId, SendMessageRequest request) =>
        api.SendAsync<MessageResponse>(HttpMethod.Post, $"api/conversations/{conversationId}/messages", request);
}
