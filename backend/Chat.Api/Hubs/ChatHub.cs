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

        public ChatHub(ChatDbContext dbContext)
        {
            _dbContext = dbContext;
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

            var isMember = await _dbContext.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

            if (!isMember)
            {
                throw new HubException("You are not a member of this conversation.");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, conversationIdStr);
        }

        public async Task LeaveConversation(string conversationIdStr)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, conversationIdStr);
        }
    }
}