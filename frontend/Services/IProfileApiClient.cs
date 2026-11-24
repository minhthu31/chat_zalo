using Chat.Client.Models;

namespace Chat.Client.Services;

// API hồ sơ và ảnh đại diện (hợp đồng Sprint 3, mục 2)
public interface IProfileApiClient
{
    Task<ProfileDto> GetAsync(Guid userId);                 // GET    /api/users/{userId}/profile
    Task<ProfileDto> UpdateMyAsync(SaveProfileRequest r);   // PUT    /api/users/me/profile
    Task<ProfileDto> UploadAvatarAsync(Stream content, string fileName, string contentType); // PUT /api/users/me/avatar (multipart, trường "file")
    Task DeleteAvatarAsync();                               // DELETE /api/users/me/avatar -> 204
}
