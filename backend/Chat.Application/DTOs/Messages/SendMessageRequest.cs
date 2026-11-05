using System;

namespace Chat.Application.DTOs.Messages
{
    public class SendMessageRequest
    {
        public string Body { get; set; } = string.Empty;
        public Guid ClientMessageId { get; set; }
    }
}