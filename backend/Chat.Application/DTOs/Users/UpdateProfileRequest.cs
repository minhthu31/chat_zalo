
using System.ComponentModel.DataAnnotations;

namespace Chat.Application.DTOs.Users;

public sealed class UpdateProfileRequest
{
    [Required(ErrorMessage = "DisplayName is required.")]
    [StringLength(100,
        ErrorMessage = "DisplayName must not exceed 100 characters.")]
    public string DisplayName { get; set; } = string.Empty;
}
