namespace Chat.Application.DTOs.Profiles;

// PUT /api/users/me/profile; the existing PATCH /api/users/me contract is unchanged.
public sealed class UpdatePublicProfileRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
}
