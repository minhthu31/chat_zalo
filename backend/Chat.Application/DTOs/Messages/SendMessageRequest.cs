using System.ComponentModel.DataAnnotations;

namespace Chat.Application.DTOs.Messages;

public sealed class SendMessageRequest
{
    public Guid ClientMessageId { get; set; }
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Body { get; set; } = string.Empty;
}
