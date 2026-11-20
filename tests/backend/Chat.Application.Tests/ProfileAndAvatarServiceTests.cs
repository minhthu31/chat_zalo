using System.Text;
using Chat.Application.DTOs.Profiles;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Chat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Chat.Application.Tests;

public class ProfileAndAvatarServiceTests
{
    private static readonly byte[] ValidPngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jG3sAAAAASUVORK5CYII=");
    private static readonly byte[] ValidJpegBytes = Convert.FromBase64String("/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD8qqKKKAP/2Q==");
    private static readonly byte[] ValidWebpBytes = Convert.FromBase64String("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA");

    private sealed class InMemoryFileStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();

        public Task WriteAsync(string storageKey, Stream content, CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            Files[storageKey] = ms.ToArray();
            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            if (!Files.TryGetValue(storageKey, out var bytes))
            {
                throw new FileNotFoundException("File not found in storage.", storageKey);
            }
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Files.Remove(storageKey);
            return Task.CompletedTask;
        }
    }

    private static ChatDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ChatDbContext(options);
    }

    private static ApplicationUser SeedUser(ChatDbContext db, string username, string displayName, string bio = "")
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
            DisplayName = displayName,
            Bio = bio,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task ProfileService_GetAsync_ValidationAndSuccess()
    {
        var db = CreateContext(Guid.NewGuid().ToString());
        var service = new ProfileService(db);

        // Invalid IDs
        var ex1 = await Assert.ThrowsAsync<ProfileServiceException>(() => service.GetAsync(Guid.Empty, Guid.NewGuid()));
        Assert.Equal(400, ex1.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ex1.Code);

        var ex2 = await Assert.ThrowsAsync<ProfileServiceException>(() => service.GetAsync(Guid.NewGuid(), Guid.Empty));
        Assert.Equal(400, ex2.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ex2.Code);

        // User Not Found
        var ex3 = await Assert.ThrowsAsync<ProfileServiceException>(() => service.GetAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(404, ex3.StatusCode);
        Assert.Equal("USER_NOT_FOUND", ex3.Code);

        // Success - Preserves 80-char DisplayName
        var longDisplayName = new string('x', 80);
        var user = SeedUser(db, "longuser", longDisplayName, "My test bio");

        var profile = await service.GetAsync(Guid.NewGuid(), user.Id);
        Assert.Equal(user.Id, profile.Id);
        Assert.Equal("longuser", profile.Username);
        Assert.Equal(longDisplayName, profile.DisplayName);
        Assert.Equal("My test bio", profile.Bio);
        Assert.Null(profile.AvatarUrl);
    }

    [Fact]
    public async Task ProfileService_UpdateAsync_ValidationRules()
    {
        var db = CreateContext(Guid.NewGuid().ToString());
        var service = new ProfileService(db);
        var user = SeedUser(db, "updateuser", "Initial Name", "Initial Bio");

        // Empty user ID or null request
        await Assert.ThrowsAsync<ProfileServiceException>(() => service.UpdateAsync(Guid.Empty, new UpdatePublicProfileRequest()));
        await Assert.ThrowsAsync<ProfileServiceException>(() => service.UpdateAsync(user.Id, null!));

        // DisplayName empty or whitespace
        var exEmpty = await Assert.ThrowsAsync<ProfileServiceException>(() =>
            service.UpdateAsync(user.Id, new UpdatePublicProfileRequest { DisplayName = "" }));
        Assert.Equal("INVALID_DISPLAY_NAME", exEmpty.Code);

        var exWs = await Assert.ThrowsAsync<ProfileServiceException>(() =>
            service.UpdateAsync(user.Id, new UpdatePublicProfileRequest { DisplayName = "   " }));
        Assert.Equal("INVALID_DISPLAY_NAME", exWs.Code);

        // DisplayName > 50 chars
        var exLongName = await Assert.ThrowsAsync<ProfileServiceException>(() =>
            service.UpdateAsync(user.Id, new UpdatePublicProfileRequest { DisplayName = new string('a', 51) }));
        Assert.Equal("INVALID_DISPLAY_NAME", exLongName.Code);

        // Bio > 200 chars
        var exLongBio = await Assert.ThrowsAsync<ProfileServiceException>(() =>
            service.UpdateAsync(user.Id, new UpdatePublicProfileRequest { DisplayName = "Valid Name", Bio = new string('b', 201) }));
        Assert.Equal("INVALID_BIO", exLongBio.Code);

        // Success - trims DisplayName and saves Bio
        var updated = await service.UpdateAsync(user.Id, new UpdatePublicProfileRequest
        {
            DisplayName = "  Trimmed Name  ",
            Bio = "Updated Bio"
        });

        Assert.Equal("Trimmed Name", updated.DisplayName);
        Assert.Equal("Updated Bio", updated.Bio);

        // Check DB
        var userFromDb = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal("Trimmed Name", userFromDb.DisplayName);
        Assert.Equal("Updated Bio", userFromDb.Bio);
    }

    [Fact]
    public async Task AvatarService_SetAsync_ValidatesFileAndStorageLifecycle()
    {
        var db = CreateContext(Guid.NewGuid().ToString());
        var storage = new InMemoryFileStorage();
        var service = new AvatarService(db, storage, NullLogger<AvatarService>.Instance);
        var user = SeedUser(db, "avataruser", "Avatar User");

        // 1. Invalid arguments
        await Assert.ThrowsAsync<ProfileServiceException>(() =>
            service.SetAsync(Guid.Empty, "avatar.png", "image/png", ValidPngBytes.Length, new MemoryStream(ValidPngBytes)));

        var exZero = await Assert.ThrowsAsync<ProfileServiceException>(() =>
            service.SetAsync(user.Id, "avatar.png", "image/png", 0, new MemoryStream([])));
        Assert.Equal("VALIDATION_ERROR", exZero.Code);

        // 2. Oversized > 2 MiB
        var exTooLarge = await Assert.ThrowsAsync<ProfileServiceException>(() =>
            service.SetAsync(user.Id, "avatar.png", "image/png", 2 * 1024 * 1024 + 1, new MemoryStream(new byte[2 * 1024 * 1024 + 1])));
        Assert.Equal(413, exTooLarge.StatusCode);
        Assert.Equal("FILE_TOO_LARGE", exTooLarge.Code);

        // 3. Unsupported extension
        var exTxt = await Assert.ThrowsAsync<ProfileServiceException>(() =>
            service.SetAsync(user.Id, "avatar.txt", "text/plain", 10, new MemoryStream(Encoding.UTF8.GetBytes("1234567890"))));
        Assert.Equal(415, exTxt.StatusCode);
        Assert.Equal("UNSUPPORTED_FILE_TYPE", exTxt.Code);

        // 4. Fake PNG content
        var exFake = await Assert.ThrowsAsync<ProfileServiceException>(() =>
            service.SetAsync(user.Id, "fake.png", "image/png", 10, new MemoryStream(Encoding.UTF8.GetBytes("fake bytes"))));
        Assert.Equal(415, exFake.StatusCode);
        Assert.Equal("UNSUPPORTED_FILE_TYPE", exFake.Code);

        // 5. Success - Set first avatar (PNG)
        var p1 = await service.SetAsync(user.Id, "avatar.png", "image/png", ValidPngBytes.Length, new MemoryStream(ValidPngBytes));
        Assert.Equal($"/api/users/{user.Id}/avatar", p1.AvatarUrl);

        var userDb1 = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.NotNull(userDb1.AvatarFileId);
        var firstFileId = userDb1.AvatarFileId.Value;

        var file1 = await db.StoredFiles.SingleAsync(f => f.Id == firstFileId);
        Assert.Equal("Avatar", file1.UsageType);
        Assert.Equal("Active", file1.Status);
        Assert.Equal("image/png", file1.ContentType);
        Assert.True(storage.Files.ContainsKey(file1.StorageKey));

        // 6. Replace with second avatar (JPEG)
        var p2 = await service.SetAsync(user.Id, "avatar.jpg", "image/jpeg", ValidJpegBytes.Length, new MemoryStream(ValidJpegBytes));
        var userDb2 = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.NotEqual(firstFileId, userDb2.AvatarFileId!.Value);
        var secondFileId = userDb2.AvatarFileId.Value;

        // Old StoredFile is Inactive and old file deleted from storage
        var oldFile = await db.StoredFiles.SingleAsync(f => f.Id == firstFileId);
        Assert.Equal("Inactive", oldFile.Status);
        Assert.False(storage.Files.ContainsKey(file1.StorageKey));

        // New StoredFile is Active and in storage
        var newFile = await db.StoredFiles.SingleAsync(f => f.Id == secondFileId);
        Assert.Equal("Active", newFile.Status);
        Assert.Equal("image/jpeg", newFile.ContentType);
        Assert.True(storage.Files.ContainsKey(newFile.StorageKey));

        // 7. Open content
        var contentResult = await service.OpenContentAsync(Guid.NewGuid(), user.Id);
        Assert.NotNull(contentResult);
        Assert.Equal("image/jpeg", contentResult.ContentType);
        using var readMs = new MemoryStream();
        await contentResult.Content.CopyToAsync(readMs);
        Assert.Equal(ValidJpegBytes, readMs.ToArray());

        // 8. Remove avatar
        await service.RemoveAsync(user.Id);
        var userDb3 = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Null(userDb3.AvatarFileId);
        Assert.False(storage.Files.ContainsKey(newFile.StorageKey));

        var openAfterDelete = await service.OpenContentAsync(Guid.NewGuid(), user.Id);
        Assert.Null(openAfterDelete);

        // 9. Remove again when no avatar (idempotent)
        await service.RemoveAsync(user.Id);
    }
}
