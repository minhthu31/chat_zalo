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

        public ChatNotificationService(IHubContext<ChatHub> hubContext, ChatDbContext dbContext)
        {
            _hubContext = hubContext;
            _dbContext = dbContext;
        }

        public async Task NotifyConversationChangedAsync(Guid conversationId, Guid eventId, long latestSeq)
        {
            // Lấy danh sách UserId thực tế còn hoạt động (LeftAt == null) trong conversation từ database
            var activeUserIds = await _dbContext.ConversationMembers
                .Where(cm => cm.ConversationId == conversationId && cm.LeftAt == null)
                .Select(cm => cm.UserId.ToString())
                .ToListAsync();

            if (activeUserIds.Any())
            {
                // Chỉ gửi sự kiện đến những user thực sự có quyền hợp lệ, ngăn chặn user đã rời nhóm nhận dữ liệu
                await _hubContext.Clients.Users(activeUserIds).SendAsync("ConversationChanged", new
                {
                    eventId = eventId,
                    conversationId = conversationId,
                    latestSeq = latestSeq
                });
            }
        }
    }
}