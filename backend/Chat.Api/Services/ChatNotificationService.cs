using Microsoft.AspNetCore.SignalR;
using chat_zalo.Hubs;

namespace chat_zalo.Services
{
	public interface IChatNotificationService
	{
		Task NotifyConversationChangedAsync(Guid conversationId, Guid eventId, long latestSeq);
	}

	public class ChatNotificationService : IChatNotificationService
	{
		private readonly IHubContext<ChatHub> _hubContext;

		public ChatNotificationService(IHubContext<ChatHub> hubContext)
		{
			_hubContext = hubContext;
		}

		public async Task NotifyConversationChangedAsync(Guid conversationId, Guid eventId, long latestSeq)
		{
			await _hubContext.Clients.Group(conversationId.ToString()).SendAsync("ConversationChanged", new
			{
				eventId = eventId,
				conversationId = conversationId,
				latestSeq = latestSeq
			});
		}
	}
}