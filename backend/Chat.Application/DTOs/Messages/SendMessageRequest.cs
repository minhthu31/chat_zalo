<<<<<<< HEAD
using System;

namespace Chat.Application.DTOs.Messages
{
    public class SendMessageRequest
    {
        public string Body { get; set; } = string.Empty;
        public Guid ClientMessageId { get; set; }
    }
=======
namespace Chat.Application.DTOs.Messages;

public class SendMessageRequest
{
    public Guid ClientMessageId { get; set; }

    public string Body { get; set; } = string.Empty;
>>>>>>> f50182b (Implement CHAT-16 group message visibility)
}