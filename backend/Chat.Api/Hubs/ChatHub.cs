using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Chat.Application.Interfaces;
using Chat.Infrastructure.Data;

namespace chat_zalo.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ChatDbContext _dbContext;
        private readonly IChatConnectionTracker _tracker;
        private readonly IGroupPermissionService _groupPermissions;

        public ChatHub(
            ChatDbContext dbContext,
            IChatConnectionTracker tracker,
            IGroupPermissionService groupPermissions)
        {
            _dbContext = dbContext;
            _tracker = tracker;
            _groupPermissions = groupPermissions;
        }

        public async Task JoinConversation(string conversationIdStr)
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

            var kind = await _dbContext.Conversations
                .AsNoTracking()
                .Where(c => c.Id == conversationId)
                .Select(c => c.Kind)
                .FirstOrDefaultAsync();

            if (kind == "Group")
            {
                // Nhóm: dùng dịch vụ phân quyền chung (S2-09).
                // Từ chối khi không còn là thành viên đang hoạt động hoặc nhóm đã giải tán.
                // Blocks không áp dụng cho việc Join nhóm; Blocks trong nhóm chỉ lọc tin nhắn.
                var permission = await _groupPermissions.CanJoinRealtimeAsync(conversationId, userId);
                if (!permission.Allowed)
                {
                    throw new HubException(permission.ErrorCode!);
                }
            }
            else
            {
                // Chat 1-1: giữ nguyên quy tắc cũ.
                var isMember = await _dbContext.ConversationMembers
                    .AnyAsync(cm => cm.ConversationId == conversationId
                                 && cm.UserId == userId
                                 && cm.LeftAt == null);

                if (!isMember)
                {
                    throw new HubException("You are not a member of this conversation.");
                }

                var blocked = await _dbContext.UserBlocks.AsNoTracking().AnyAsync(b =>
                    b.BlockedUserId == userId && _dbContext.ConversationMembers.Any(peer =>
                        peer.ConversationId == conversationId && peer.UserId == b.BlockerUserId));
                if (blocked)
                    throw new HubException("Conversation access denied.");
            }

            string groupName = conversationId.ToString("D");

            // Sử dụng đúng tên phương thức AddSubscription
            _tracker.AddSubscription(Context.ConnectionId, userId, conversationId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        public async Task LeaveConversation(string conversationIdStr)
        {
            if (!Guid.TryParse(conversationIdStr, out var conversationId))
            {
                throw new HubException("Invalid conversation ID format.");
            }

            string groupName = conversationId.ToString("D");

            // Sử dụng đúng tên phương thức RemoveSubscription
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