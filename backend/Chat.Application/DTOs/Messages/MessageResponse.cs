using System;

namespace Chat.Application.DTOs.Messages
{
    public class MessageResponse
    {
        public Guid Id { get; set; }
        public Guid ConversationId { get; set; }
        public Guid SenderId { get; set; }
        public string Content { get; set; } = string.Empty;
        public long Seq { get; set; }
        public string ClientMessageId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}