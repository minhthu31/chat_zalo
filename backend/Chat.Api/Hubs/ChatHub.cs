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
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(
            ChatDbContext dbContext,
            IChatConnectionTracker tracker,
            IGroupPermissionService groupPermissions,
            ILogger<ChatHub> logger)
        {
            _dbContext = dbContext;
            _tracker = tracker;
            _groupPermissions = groupPermissions;
            _logger = logger;
        }

        public async Task JoinConversation(string conversationIdStr)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(conversationIdStr) ||
                    !Guid.TryParse(conversationIdStr, out var conversationId))
                {
                    throw new HubException("Invalid conversation ID format.");
                }

                var userIdStr = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? Context.User?.FindFirst("sub")?.Value;

                if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
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
                    var permission = await _groupPermissions.CanJoinRealtimeAsync(conversationId, userId);
                    if (!permission.Allowed)
                    {
                        throw new HubException(permission.ErrorCode!);
                    }
                }
                else
                {
                    var isMember = await _dbContext.ConversationMembers
                        .AsNoTracking()
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
                    {
                        throw new HubException("Conversation access denied.");
                    }
                }

                var groupName = conversationId.ToString("D");

                _tracker.AddSubscription(Context.ConnectionId, userId, conversationId);
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

            }
            catch (HubException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to join conversation for connection {ConnectionId}", Context.ConnectionId);
                throw new HubException("Không thể tham gia hội thoại. Vui lòng thử lại sau.");
            }
        }

        public async Task LeaveConversation(string conversationIdStr)
        {
            if (string.IsNullOrWhiteSpace(conversationIdStr) ||
                !Guid.TryParse(conversationIdStr, out var conversationId))
            {
                throw new HubException("Invalid conversation ID format.");
            }

            var groupName = conversationId.ToString("D");

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
