using Chat.Application.DTOs.Files;
using Chat.Application.DTOs.Profiles;

namespace Chat.Application.Interfaces;

public interface IAvatarService
{
    Task<ProfileDto> SetAsync(
        Guid currentUserId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream content,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid currentUserId, CancellationToken cancellationToken = default);
    Task<FileContentResult?> OpenContentAsync(Guid currentUserId, Guid userId, CancellationToken cancellationToken = default);
}
