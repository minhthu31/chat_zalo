using Chat.Client.Models;

namespace Chat.Client.Services;

// Bản thật của IMessageReceiptApiClient
public class MessageReceiptApiClient : IMessageReceiptApiClient
{
    private readonly ApiRequestSender _api;

    public MessageReceiptApiClient(ApiRequestSender api)
    {
        _api = api;
    }

    public Task<ConversationReceiptsDto> GetAsync(Guid conversationId, IReadOnlyCollection<Guid>? messageIds = null)
    {
        var url = $"api/conversations/{conversationId}/receipts";
        if (messageIds is { Count: > 0 })
            url += "?messageIds=" + string.Join(",", messageIds.Take(100)); // hợp đồng: tối đa 100 mã
        return _api.SendAsync<ConversationReceiptsDto>(HttpMethod.Get, url);
    }

    public Task<ReceiptDto> MarkDeliveredAsync(Guid conversationId, long seq) =>
        _api.SendAsync<ReceiptDto>(HttpMethod.Put, $"api/conversations/{conversationId}/receipts/delivered", new MarkReceiptRequest { Seq = seq });

    public Task<ReceiptDto> MarkReadAsync(Guid conversationId, long seq) =>
        _api.SendAsync<ReceiptDto>(HttpMethod.Put, $"api/conversations/{conversationId}/receipts/read", new MarkReceiptRequest { Seq = seq });
}
