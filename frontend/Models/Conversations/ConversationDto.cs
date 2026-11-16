namespace Chat.Client.Models;

public sealed record ConversationDto(Guid Id, string Kind, long LastMessageSeq);
