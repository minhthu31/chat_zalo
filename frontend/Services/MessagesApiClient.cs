using Chat.Client.Models.Messages;

namespace Chat.Client.Services;

public sealed class MessagesApiClient(ChatApiClient api)
{
    public Task<IReadOnlyList<MessageResponse>> GetLatestAsync(
        Guid conversationId, long afterSeq = 0, int limit = 20, CancellationToken ct = default) =>
        api.GetMessagesAsync(conversationId, afterSeq: afterSeq, limit: limit, ct: ct);

    public Task<IReadOnlyList<MessageResponse>> GetOlderAsync(
        Guid conversationId, long beforeSeq, int limit = 20, CancellationToken ct = default) =>
        api.GetMessagesAsync(conversationId, beforeSeq: beforeSeq, limit: limit, ct: ct);

    public Task<MessageResponse> SendAsync(
        Guid conversationId, string body, Guid clientMessageId, CancellationToken ct = default) =>
        api.SendMessageAsync(conversationId, body, clientMessageId, ct);
}
