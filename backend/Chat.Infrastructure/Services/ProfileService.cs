using Chat.Application.DTOs.Profiles;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Services;

public sealed class ProfileService(ChatDbContext db) : IProfileService
{
    public async Task<ProfileDto> GetAsync(Guid currentUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (currentUserId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ProfileServiceException(400, "VALIDATION_ERROR", "Invalid user identifier.");
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new ProfileServiceException(404, "USER_NOT_FOUND", "User not found.");
        }

        return new ProfileDto
        {
            Id = user.Id,
            Username = user.UserName ?? string.Empty,
            DisplayName = user.DisplayName,
            Bio = user.Bio,
            AvatarUrl = user.AvatarFileId.HasValue ? $"/api/users/{user.Id}/avatar" : null
        };
    }

    public async Task<ProfileDto> UpdateAsync(Guid currentUserId, UpdatePublicProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (currentUserId == Guid.Empty || request is null)
        {
            throw new ProfileServiceException(400, "VALIDATION_ERROR", "Invalid profile update request.");
        }

        var displayName = (request.DisplayName ?? string.Empty).Trim();
        if (displayName.Length < 1 || displayName.Length > 50)
        {
            throw new ProfileServiceException(400, "INVALID_DISPLAY_NAME", "Display name must be between 1 and 50 characters.");
        }

        var bio = request.Bio ?? string.Empty;
        if (bio.Length > 200)
        {
            throw new ProfileServiceException(400, "INVALID_BIO", "Bio must not exceed 200 characters.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
        if (user is null)
        {
            throw new ProfileServiceException(404, "USER_NOT_FOUND", "User not found.");
        }

        user.DisplayName = displayName;
        user.Bio = bio;

        await db.SaveChangesAsync(cancellationToken);

        return new ProfileDto
        {
            Id = user.Id,
            Username = user.UserName ?? string.Empty,
            DisplayName = user.DisplayName,
            Bio = user.Bio,
            AvatarUrl = user.AvatarFileId.HasValue ? $"/api/users/{user.Id}/avatar" : null
        };
    }
}
