using Chat.Client.Models;
namespace Chat.Client.Services;

public interface IChatMessages
{
    Task<List<MessageResponse>> GetLatestAsync(Guid id);
    Task<List<MessageResponse>> GetOlderAsync(Guid id, long beforeSeq);
    Task<List<MessageResponse>> GetNewAsync(Guid id, long afterSeq);
    Task<MessageResponse> SendAsync(Guid id, SendMessageRequest request);
}

public sealed class MessagesApiClient(ApiRequestSender api) : IChatMessages
{
    public Task<List<MessageResponse>> GetLatestAsync(Guid id) =>
        api.SendAsync<List<MessageResponse>>(HttpMethod.Get, $"api/conversations/{id}/messages?limit=20");
    public Task<List<MessageResponse>> GetOlderAsync(Guid id, long beforeSeq) =>
        api.SendAsync<List<MessageResponse>>(HttpMethod.Get, $"api/conversations/{id}/messages?beforeSeq={beforeSeq}&limit=20");
    public Task<List<MessageResponse>> GetNewAsync(Guid id, long afterSeq) =>
        api.SendAsync<List<MessageResponse>>(HttpMethod.Get, $"api/conversations/{id}/messages?afterSeq={afterSeq}&limit=100");
    public Task<MessageResponse> SendAsync(Guid id, SendMessageRequest request) =>
        api.SendAsync<MessageResponse>(HttpMethod.Post, $"api/conversations/{id}/messages", request);
}
