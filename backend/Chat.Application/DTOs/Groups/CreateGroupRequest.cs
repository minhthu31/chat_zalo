using System.ComponentModel.DataAnnotations;

namespace Chat.Application.DTOs.Groups;

public class CreateGroupRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public List<Guid> MemberIds { get; set; } = new();
}
