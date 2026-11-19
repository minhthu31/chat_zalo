using Chat.Application.DTOs.Profiles;

namespace Chat.Application.Interfaces;

public interface IProfileService
{
    Task<ProfileDto> GetAsync(Guid currentUserId, Guid userId, CancellationToken cancellationToken = default);
    Task<ProfileDto> UpdateAsync(Guid currentUserId, UpdatePublicProfileRequest request, CancellationToken cancellationToken = default);
}
