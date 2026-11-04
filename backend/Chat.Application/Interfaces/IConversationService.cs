using Chat.Application.DTOs.Conversations;

namespace Chat.Application.Interfaces;

public interface IConversationService
{
    Task<ConversationDto> CreateDirectAsync(
        Guid currentUserId,
        Guid targetUserId);

    Task<List<ConversationDto>> GetConversationsAsync(
        Guid currentUserId);
}