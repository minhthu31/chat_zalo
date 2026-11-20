using Chat.Application.DTOs.Files;
using Chat.Application.DTOs.Profiles;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.Services;

public sealed class AvatarService(
    ChatDbContext db,
    IFileStorage storage,
    ILogger<AvatarService> logger) : IAvatarService
{
    public const long MaxAvatarBytes = 2 * 1024 * 1024; // 2.097.152 bytes

    public async Task<ProfileDto> SetAsync(
        Guid currentUserId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (currentUserId == Guid.Empty)
        {
            throw new ProfileServiceException(400, "VALIDATION_ERROR", "Invalid user identifier.");
        }

        if (sizeBytes > MaxAvatarBytes)
        {
            throw new ProfileServiceException(413, "FILE_TOO_LARGE", "Avatar file must not exceed 2 MiB.");
        }

        if (sizeBytes <= 0)
        {
            throw new ProfileServiceException(400, "VALIDATION_ERROR", "File size must be greater than zero.");
        }

        fileName = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255 || fileName.Any(char.IsControl))
        {
            throw new ProfileServiceException(400, "VALIDATION_ERROR", "Invalid file name.");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxAvatarBytes)
            {
                throw new ProfileServiceException(413, "FILE_TOO_LARGE", "Avatar file must not exceed 2 MiB.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        if (buffer.Length == 0 || buffer.Length != sizeBytes)
        {
            throw new ProfileServiceException(400, "VALIDATION_ERROR", "File content length mismatch or empty.");
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
        {
            throw new ProfileServiceException(415, "UNSUPPORTED_FILE_TYPE", "Avatar must be a JPEG, PNG, or WebP image.");
        }

        string verifiedType;
        try
        {
            verifiedType = AttachmentContentValidator.Validate(ext, buffer.ToArray());
        }
        catch (AttachmentServiceException)
        {
            throw new ProfileServiceException(415, "UNSUPPORTED_FILE_TYPE", "Unsupported or invalid avatar file content.");
        }

        var storageKey = "avatars/" + Guid.NewGuid().ToString("N");
        buffer.Position = 0;
        await storage.WriteAsync(storageKey, buffer, cancellationToken);

        string? oldStorageKey = null;
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            ApplicationUser? user;
            if (db.Database.IsRelational())
            {
                user = await db.Users
                    .FromSqlRaw("SELECT * FROM \"Users\" WHERE \"Id\" = {0} FOR UPDATE", currentUserId)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
            }

            if (user is null)
            {
                if (tx != null) await tx.RollbackAsync(cancellationToken);
                throw new ProfileServiceException(404, "USER_NOT_FOUND", "User not found.");
            }

            var newFile = new StoredFile
            {
                UploadedByUserId = currentUserId,
                ConversationId = null,
                UsageType = "Avatar",
                Status = "Active",
                StorageKey = storageKey,
                FileName = fileName,
                ContentType = verifiedType,
                SizeBytes = buffer.Length
            };
            db.StoredFiles.Add(newFile);

            if (user.AvatarFileId.HasValue)
            {
                var oldFile = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == user.AvatarFileId.Value, cancellationToken);
                if (oldFile != null)
                {
                    oldFile.Status = "Inactive";
                    oldStorageKey = oldFile.StorageKey;
                }
            }

            user.AvatarFileId = newFile.Id;
            await db.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            if (!string.IsNullOrEmpty(oldStorageKey))
            {
                try
                {
                    await storage.DeleteAsync(oldStorageKey, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to delete old avatar file from physical storage for key {StorageKey}", oldStorageKey);
                }
            }

            return new ProfileDto
            {
                Id = user.Id,
                Username = user.UserName ?? string.Empty,
                DisplayName = user.DisplayName,
                Bio = user.Bio,
                AvatarUrl = $"/api/users/{user.Id}/avatar"
            };
        }
        catch
        {
            if (tx != null)
            {
                try { await tx.RollbackAsync(CancellationToken.None); } catch { }
            }

            try
            {
                await storage.DeleteAsync(storageKey, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not delete newly written avatar file {StorageKey} after failure", storageKey);
            }
            throw;
        }
    }

    public async Task RemoveAsync(Guid currentUserId, CancellationToken cancellationToken = default)
    {
        if (currentUserId == Guid.Empty)
        {
            throw new ProfileServiceException(400, "VALIDATION_ERROR", "Invalid user identifier.");
        }

        string? oldStorageKey = null;
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            ApplicationUser? user;
            if (db.Database.IsRelational())
            {
                user = await db.Users
                    .FromSqlRaw("SELECT * FROM \"Users\" WHERE \"Id\" = {0} FOR UPDATE", currentUserId)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
            }

            if (user is null)
            {
                if (tx != null) await tx.RollbackAsync(cancellationToken);
                throw new ProfileServiceException(404, "USER_NOT_FOUND", "User not found.");
            }

            if (!user.AvatarFileId.HasValue)
            {
                if (tx != null) await tx.CommitAsync(cancellationToken);
                return;
            }

            var oldFile = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == user.AvatarFileId.Value, cancellationToken);
            if (oldFile != null)
            {
                oldFile.Status = "Inactive";
                oldStorageKey = oldFile.StorageKey;
            }

            user.AvatarFileId = null;
            await db.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (tx != null)
            {
                try { await tx.RollbackAsync(CancellationToken.None); } catch { }
            }
            throw;
        }

        if (!string.IsNullOrEmpty(oldStorageKey))
        {
            try
            {
                await storage.DeleteAsync(oldStorageKey, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete removed avatar file {StorageKey} from physical storage", oldStorageKey);
            }
        }
    }

    public async Task<FileContentResult?> OpenContentAsync(Guid currentUserId, Guid userId, CancellationToken cancellationToken = default)
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

        if (!user.AvatarFileId.HasValue)
        {
            return null;
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(
            f => f.Id == user.AvatarFileId.Value && f.UsageType == "Avatar" && f.Status == "Active",
            cancellationToken);

        if (file is null)
        {
            return null;
        }

        try
        {
            var stream = await storage.OpenReadAsync(file.StorageKey, cancellationToken);
            return new FileContentResult(stream, file.ContentType, file.FileName);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }
}
