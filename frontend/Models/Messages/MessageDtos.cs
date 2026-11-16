namespace Chat.Client.Models;

public sealed record MessageResponse(Guid Id, Guid ConversationId, Guid SenderUserId, long Seq, string Body, DateTimeOffset CreatedAt);
public sealed record SendMessageRequest(Guid ClientMessageId, string Body);
public sealed record ConversationChanged(Guid EventId, Guid ConversationId, long LatestSeq);
