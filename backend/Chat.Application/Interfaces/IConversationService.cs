using Chat.Application.DTOs.Conversations;

namespace Chat.Application.Interfaces;

public interface IConversationService
{
    /// <summary>
    /// Creates a new Direct conversation between <paramref name="currentUserId"/> and
    /// <paramref name="targetUserId"/>, or returns the existing one.
    /// The returned DTO carries <see cref="ConversationDto.IsNewlyCreated"/> to let
    /// the caller distinguish HTTP 201 from HTTP 200.
    /// </summary>
    Task<ConversationDto> CreateDirectAsync(Guid currentUserId, Guid targetUserId);

    /// <summary>
    /// Returns all conversations the current user is an active member of,
    /// ordered by most-recent activity first.
    /// </summary>
    Task<List<ConversationDto>> GetConversationsAsync(Guid currentUserId);
}
