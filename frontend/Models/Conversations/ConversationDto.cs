namespace Chat.Client.Models.Conversations;

public sealed record ConversationDto(Guid Id, string Kind, long LastMessageSeq);
