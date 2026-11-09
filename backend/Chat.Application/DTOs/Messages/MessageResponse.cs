<<<<<<< HEAD
using System;
using System.Text.Json.Serialization;

namespace Chat.Application.DTOs.Messages;

public sealed class MessageResponse
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderUserId { get; set; }
    public long Seq { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    [JsonIgnore]
    public Guid ClientMessageId { get; set; }

    [JsonIgnore]
    public bool IsNewlyCreated { get; set; }
=======
namespace Chat.Application.DTOs.Messages;

public class MessageResponse
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid SenderUserId { get; set; }

    public Guid ClientMessageId { get; set; }

    public long Seq { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
>>>>>>> f50182b (Implement CHAT-16 group message visibility)
}