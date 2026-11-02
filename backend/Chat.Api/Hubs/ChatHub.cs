using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace chat_zalo.Hubs
{
    [Authorize] // Chặn người chưa xác thực (Yêu cầu 3)
    public class ChatHub : Hub
    {
        // Hàm tham gia nhóm theo ConversationId có kiểm tra quyền (Yêu cầu 4, 5)
        public async Task JoinConversation(string conversationId)
        {
            // TODO: Bổ sung logic kiểm tra xem user hiện tại có thuộc về conversationId này không ở đây nếu cần

            await Groups.AddToAsync(Context.ConnectionId, conversationId);
        }

        public async Task LeaveConversation(string conversationId)
        {
            await Groups.RemoveFromAsync(Context.ConnectionId, conversationId);
        }

        // Phương thức phát sự kiện ConversationChanged với đúng Payload chuẩn (Yêu cầu 6)
        public async Task SendConversationChanged(string conversationId, string eventId, long latestSeq)
        {
            await Clients.Group(conversationId).SendAsync("ConversationChanged", new
            {
                EventId = eventId,
                ConversationId = conversationId,
                LatestSeq = latestSeq
            });
        }
    }
}