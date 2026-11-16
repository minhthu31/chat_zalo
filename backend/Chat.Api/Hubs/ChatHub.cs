using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Chat.Infrastructure.Data;

namespace chat_zalo.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ChatDbContext _dbContext;
        private readonly IChatConnectionTracker _tracker;

        public ChatHub(ChatDbContext dbContext, IChatConnectionTracker tracker)
        {
            _dbContext = dbContext;
            _tracker = tracker;
        }

        public async Task JoinConversation(string conversationIdStr, long? afterSeq = null)
        {
            if (!Guid.TryParse(conversationIdStr, out var conversationId))
            {
                throw new HubException("Invalid conversation ID format.");
            }

            var userIdStr = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? Context.User?.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            {
                throw new HubException("Unauthorized user.");
            }

            // Kiểm tra quyền: Phải là thành viên và chưa rời nhóm (LeftAt == null)
            var isMember = await _dbContext.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == conversationId
                             && cm.UserId == userId
                             && cm.LeftAt == null);

            if (!isMember)
            {
                throw new HubException("You are not a member of this conversation.");
            }

            string groupName = conversationId.ToString("D");

            // Đăng ký subscription và thêm vào group SignalR
            _tracker.AddSubscription(Context.ConnectionId, userId, conversationId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

            // Bổ sung theo yêu cầu S2-08 (Mục 6): Hỗ trợ reconnect và đồng bộ tin nhắn còn thiếu bằng afterSeq
            if (afterSeq.HasValue)
            {
                var missedMessages = await _dbContext.Messages
                    .Where(m => m.ConversationId == conversationId && m.Seq > afterSeq.Value)
                    .OrderBy(m => m.Seq)
                    .ToListAsync();

                // Gửi tin nhắn bù về cho riêng Client vừa reconnect
                await Clients.Caller.SendAsync("ReceiveMissedMessages", missedMessages);
            }
        }

        public async Task LeaveConversation(string conversationIdStr)
        {
            if (!Guid.TryParse(conversationIdStr, out var conversationId))
            {
                throw new HubException("Invalid conversation ID format.");
            }

            string groupName = conversationId.ToString("D");

            // Xử lý hủy subscription an toàn, không làm ảnh hưởng hội thoại khác
            _tracker.RemoveSubscription(Context.ConnectionId, conversationId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _tracker.RemoveConnection(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}