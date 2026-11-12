using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Chat.Application.Interfaces;
using chat_zalo.Hubs;
using Chat.Infrastructure.Data;

namespace chat_zalo.Services
{
    public interface IChatNotificationService : IConversationEventPublisher
    {
    }

    public class ChatNotificationService : IChatNotificationService
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ChatDbContext _dbContext;
        private readonly IChatConnectionTracker _tracker;
        private readonly IGroupPermissionService _groupPermissions;

        public ChatNotificationService(
            IHubContext<ChatHub> hubContext,
            ChatDbContext dbContext,
            IChatConnectionTracker tracker,
            IGroupPermissionService groupPermissions)
        {
            _hubContext = hubContext;
            _dbContext = dbContext;
            _tracker = tracker;
            _groupPermissions = groupPermissions;
        }

        public async Task NotifyConversationChangedAsync(Guid conversationId, Guid eventId, long latestSeq)
        {
            var kind = await _dbContext.Conversations
                .AsNoTracking()
                .Where(c => c.Id == conversationId)
                .Select(c => c.Kind)
                .FirstOrDefaultAsync();

            if (kind is null) return;

            // Tách quy tắc theo loại hội thoại:
            // - Group: quyền nhận realtime do IGroupPermissionService quyết định, KHÔNG áp Blocks.
            // - Direct: giữ quy tắc Blocks của chat 1-1.
            var activeUserIds = kind == "Group"
                ? await GetGroupRecipientsAsync(conversationId)
                : await GetDirectRecipientsAsync(conversationId);

            if (!activeUserIds.Any()) return;

            // Sử dụng đúng tên phương thức GetActiveConnectionsForConversation
            var activeConnectionIds = _tracker.GetActiveConnectionsForConversation(conversationId, activeUserIds);

            if (activeConnectionIds.Any())
            {
                await _hubContext.Clients.Clients(activeConnectionIds.ToList()).SendAsync("ConversationChanged", new
                {
                    eventId = eventId,
                    conversationId = conversationId,
                    latestSeq = latestSeq
                });
            }
        }

        // Nhóm: chỉ thành viên đang hoạt động của nhóm chưa giải tán mới nhận event.
        // Người đã Join từ trước nhưng đã rời/bị xóa, hoặc nhóm đã giải tán, đều bị loại.
        // Blocks trong nhóm chỉ ẩn tin nhắn khi tải lịch sử, không cắt thông báo của người khác.
        private async Task<List<Guid>> GetGroupRecipientsAsync(Guid conversationId)
        {
            var memberIds = await _dbContext.ConversationMembers
                .AsNoTracking()
                .Where(cm => cm.ConversationId == conversationId && cm.LeftAt == null)
                .Select(cm => cm.UserId)
                .ToListAsync();

            var recipients = new List<Guid>();
            foreach (var userId in memberIds)
            {
                var permission = await _groupPermissions.CanReceiveRealtimeAsync(conversationId, userId);
                if (permission.Allowed)
                {
                    recipients.Add(userId);
                }
            }

            return recipients;
        }

        // Chat 1-1: giữ nguyên quy tắc cũ (loại người bị đối phương chặn).
        private Task<List<Guid>> GetDirectRecipientsAsync(Guid conversationId)
        {
            return _dbContext.ConversationMembers
                .Where(cm => cm.ConversationId == conversationId && cm.LeftAt == null)
                .Where(cm => !_dbContext.UserBlocks.Any(b => b.BlockedUserId == cm.UserId
                    && _dbContext.ConversationMembers.Any(peer =>
                        peer.ConversationId == conversationId && peer.UserId == b.BlockerUserId)))
                .Select(cm => cm.UserId)
                .ToListAsync();
        }
    }
}