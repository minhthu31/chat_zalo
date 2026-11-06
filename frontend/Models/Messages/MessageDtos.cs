namespace Chat.Client.Models.Messages;

public sealed record MessageResponse(
    Guid Id,
    Guid ConversationId,
    Guid SenderUserId,
    long Seq,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record SendMessageRequest(Guid ClientMessageId, string Body);

public sealed record ConversationChangedEvent(
    Guid EventId,
    Guid ConversationId,
    long LatestSeq);

public sealed record CsrfTokenResponse(string Token);
