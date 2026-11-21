using Chat.Application.DTOs.Receipts;

namespace Chat.Application.Interfaces;

public interface IMessageReceiptService
{
    const int MaxMessageIds = 100;

    Task<ConversationReceiptsDto> GetAsync(Guid currentUserId, Guid conversationId,
        CancellationToken cancellationToken = default, IReadOnlyCollection<Guid>? messageIds = null);
    Task<ReceiptDto> MarkDeliveredAsync(
        Guid currentUserId,
        Guid conversationId,
        UpdateReceiptRequest request,
        CancellationToken cancellationToken = default);
    Task<ReceiptDto> MarkReadAsync(
        Guid currentUserId,
        Guid conversationId,
        UpdateReceiptRequest request,
        CancellationToken cancellationToken = default);
}
