using Chat.Application.DTOs.Receipts;

namespace Chat.Application.Interfaces;

public interface IMessageReceiptService
{
    Task<ConversationReceiptsDto> GetAsync(Guid currentUserId, Guid conversationId, CancellationToken cancellationToken = default);
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
