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

            // Kiểm tra membership hợp lệ và LeftAt == null
            var isMember = await _dbContext.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == conversationId
                             && cm.UserId == userId
                             && cm.LeftAt == null);

            if (!isMember)
            {
                throw new HubException("You are not a member of this conversation.");
            }

            string groupName = conversationId.ToString("D");
            _tracker.AddConnection(Context.ConnectionId, userId, conversationId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        public async Task LeaveConversation(string conversationIdStr)
        {
            if (!Guid.TryParse(conversationIdStr, out var conversationId))
            {
                throw new HubException("Invalid conversation ID format.");
            }

            string groupName = conversationId.ToString("D");
            _tracker.RemoveConnection(Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _tracker.RemoveConnection(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}