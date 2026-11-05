using System;

namespace Chat.Application.DTOs.Messages
{
    public class MessageResponse
    {
        public Guid Id { get; set; }
        public Guid ConversationId { get; set; }
        public Guid SenderUserId { get; set; }
        public string Body { get; set; } = string.Empty;
        public long Seq { get; set; }
        public Guid ClientMessageId { get; set; }
        public DateTimeOffset CreatedAt { get; set; } // Chuẩn UTC DateTimeOffset theo DATABASE_RULES
    }
}