using System.ComponentModel.DataAnnotations;

namespace Chat.Application.DTOs.Groups;

public class AddGroupMemberRequest
{
    [Required]
    public Guid UserId { get; set; }
}
