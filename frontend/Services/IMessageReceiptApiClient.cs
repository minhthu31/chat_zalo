using Chat.Client.Models;

namespace Chat.Client.Services;

// Xác nhận đã nhận / đã xem tin nhắn (hợp đồng Sprint 3, mục 5)
public interface IMessageReceiptApiClient
{
    // GET /api/conversations/{id}/receipts. Nhóm: truyền mã các tin trên trang (tối đa 100) để lấy trạng thái từng tin
    Task<ConversationReceiptsDto> GetAsync(Guid conversationId, IReadOnlyCollection<Guid>? messageIds = null);
    Task<ReceiptDto> MarkDeliveredAsync(Guid conversationId, long seq);           // PUT .../receipts/delivered {seq}
    Task<ReceiptDto> MarkReadAsync(Guid conversationId, long seq);                // PUT .../receipts/read {seq} (tự nâng delivered nếu cần)
}
