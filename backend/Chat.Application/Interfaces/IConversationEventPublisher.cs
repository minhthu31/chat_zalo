namespace Chat.Application.Interfaces;

/// <summary>
/// Publishes conversation change notifications after message persistence commits.
/// </summary>
public interface IConversationEventPublisher
{
    Task NotifyConversationChangedAsync(
        Guid conversationId,
        Guid eventId,
        long latestSeq);
}
