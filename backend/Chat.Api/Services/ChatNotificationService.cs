using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using chat_zalo.Hubs;
using Chat.Infrastructure.Data;

namespace chat_zalo.Services
{
    public interface IChatNotificationService
    {
        Task NotifyConversationChangedAsync(Guid conversationId, Guid eventId, long latestSeq);
    }

    public class ChatNotificationService : IChatNotificationService
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ChatDbContext _dbContext;
        private readonly IChatConnectionTracker _tracker;

        public ChatNotificationService(IHubContext<ChatHub> hubContext, ChatDbContext dbContext, IChatConnectionTracker tracker)
        {
            _hubContext = hubContext;
            _dbContext = dbContext;
            _tracker = tracker;
        }

        public async Task NotifyConversationChangedAsync(Guid conversationId, Guid eventId, long latestSeq)
        {
            // Lấy danh sách UserId có membership hợp lệ (LeftAt == null) trong database tại thời điểm phát event
            var activeUserIds = await _dbContext.ConversationMembers
                .Where(cm => cm.ConversationId == conversationId && cm.LeftAt == null)
                .Select(cm => cm.UserId)
                .ToListAsync();

            if (!activeUserIds.Any()) return;

            // Lấy chính xác các connection đang kết nối VÀ thuộc về user hợp lệ trong DB
            var activeConnectionIds = _tracker.GetConnectionsForConversation(conversationId, activeUserIds);

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
    }
}