using System.ComponentModel.DataAnnotations;

namespace Chat.Application.DTOs.Conversations;

public class CreateDirectConversationRequest
{
    [Required]
    public Guid TargetUserId { get; set; }
}
