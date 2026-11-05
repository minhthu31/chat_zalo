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
            var activeUserIds = await _dbContext.ConversationMembers
                .Where(cm => cm.ConversationId == conversationId && cm.LeftAt == null)
                .Select(cm => cm.UserId)
                .ToListAsync();

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
    }
}