using System.ComponentModel.DataAnnotations;

namespace Chat.Application.DTOs.Groups;

public class AdminTransferRequest
{
    [Required]
    public Guid TargetUserId { get; set; }
}
