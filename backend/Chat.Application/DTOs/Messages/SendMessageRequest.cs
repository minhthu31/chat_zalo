using System;

namespace Chat.Application.DTOs.Messages
{
    public class SendMessageRequest
    {
        public Guid ConversationId { get; set; }
        public Guid ReceiverId { get; set; }
        public string Content { get; set; } = string.Empty;
        public string ClientMessageId { get; set; } = string.Empty;
    }
}