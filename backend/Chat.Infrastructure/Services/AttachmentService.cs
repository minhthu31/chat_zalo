using Chat.Application.DTOs.Attachments;
using Chat.Application.DTOs.Files;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.Services;

public sealed class AttachmentService(ChatDbContext db, IFileStorage storage,
    IGroupPermissionService groups, ILogger<AttachmentService> logger) : IAttachmentService
{
    public async Task<AttachmentDto> UploadAsync(Guid currentUserId, Guid conversationId, string fileName,
        string contentType, long sizeBytes, Stream content, CancellationToken cancellationToken = default)
    {
        await EnsureAccess(currentUserId, conversationId, sending: true, cancellationToken);
        if (sizeBytes > AttachmentContentValidator.MaxBytes) throw TooLarge();
        if (sizeBytes <= 0) throw InvalidFile();
        fileName = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255 || fileName.Any(char.IsControl)) throw InvalidFile();
        // A bounded buffer caps each upload at 10 MiB, including non-seekable streams.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920]; int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > AttachmentContentValidator.MaxBytes) throw TooLarge();
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        if (buffer.Length == 0 || buffer.Length != sizeBytes) throw InvalidFile();
        var verifiedType = AttachmentContentValidator.Validate(Path.GetExtension(fileName).ToLowerInvariant(), buffer.ToArray());
        var file = new StoredFile
        {
            UploadedByUserId = currentUserId, ConversationId = conversationId,
            UsageType = "Attachment", Status = "Pending", FileName = fileName,
            ContentType = verifiedType, SizeBytes = buffer.Length,
            StorageKey = "attachments/" + Guid.NewGuid().ToString("N")
        };
        buffer.Position = 0;
        await storage.WriteAsync(file.StorageKey, buffer, cancellationToken);
        try
        {
            // Membership/Blocks may have changed while the file was being read.
            await EnsureAccess(currentUserId, conversationId, sending: true, cancellationToken);
            db.StoredFiles.Add(file);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try { await storage.DeleteAsync(file.StorageKey, CancellationToken.None); }
            catch (Exception ex) { logger.LogError(ex, "Could not remove an uncommitted upload {FileId}", file.Id); }
            throw;
        }
        return new AttachmentDto
        {
            Id = file.Id, FileName = file.FileName, ContentType = file.ContentType, SizeBytes = file.SizeBytes,
            ContentUrl = $"/api/attachments/{file.Id}/content", DownloadUrl = $"/api/attachments/{file.Id}/download"
        };
    }

    public async Task<FileContentResult> OpenContentAsync(Guid currentUserId, Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var file = await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(
            f => f.Id == attachmentId && f.UsageType == "Attachment", cancellationToken);
        if (file?.ConversationId is not Guid conversationId) throw Missing();
        var member = await EnsureAccess(currentUserId, conversationId, sending: false, cancellationToken);
        if (file.Status == "Pending")
        {
            if (file.UploadedByUserId != currentUserId) throw Denied();
        }
        else if (file.Status == "Attached")
        {
            var message = await db.MessageAttachments.AsNoTracking()
                .Where(a => a.StoredFileId == attachmentId).Select(a => a.Message).SingleOrDefaultAsync(cancellationToken);
            if (message is null) throw Missing();
            if (message.ConversationId != conversationId || message.Seq < member.JoinedSeq) throw Denied();
            if (member.IsGroup && await db.UserBlocks.AnyAsync(b =>
                (b.BlockerUserId == message.SenderUserId && b.BlockedUserId == currentUserId) ||
                (b.BlockerUserId == currentUserId && b.BlockedUserId == message.SenderUserId), cancellationToken))
                throw Denied();
        }
        else throw Missing();
        try { return new FileContentResult(await storage.OpenReadAsync(file.StorageKey, cancellationToken), file.ContentType, file.FileName); }
        catch (FileNotFoundException) { throw Missing(); }
        catch (DirectoryNotFoundException) { throw Missing(); }
    }

    private async Task<(bool IsGroup, long JoinedSeq)> EnsureAccess(Guid userId, Guid id, bool sending, CancellationToken ct)
    {
        if (userId == Guid.Empty || id == Guid.Empty) throw new AttachmentServiceException(400, "VALIDATION_ERROR", "Invalid identifier.");
        var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null) throw new AttachmentServiceException(404, "NOT_FOUND", "Conversation not found.");
        var member = await db.ConversationMembers.AsNoTracking().SingleOrDefaultAsync(m =>
            m.ConversationId == id && m.UserId == userId && m.LeftAt == null, ct);
        if (conversation.Kind == "Group")
        {
            var permission = sending ? await groups.CanSendMessageAsync(id, userId, ct)
                : await groups.CanViewMessagesAsync(id, userId, ct);
            if (!permission.Allowed || member is null)
                throw new AttachmentServiceException(permission.HttpStatus ?? 403,
                    permission.ErrorCode ?? "GROUP_ACCESS_DENIED", "Group access denied.");
            return (true, member.JoinedSeq);
        }
        if (conversation.Kind != "Direct" || member is null) throw Denied();
        var direct = await db.DirectConversations.AsNoTracking().SingleOrDefaultAsync(d => d.ConversationId == id, ct);
        if (direct is null || (direct.UserLowId != userId && direct.UserHighId != userId)) throw Denied();
        var peer = direct.UserLowId == userId ? direct.UserHighId : direct.UserLowId;
        if (await db.UserBlocks.AnyAsync(b => b.BlockerUserId == peer && b.BlockedUserId == userId, ct)) throw Denied();
        if (sending && (await db.UserBlocks.AnyAsync(b => b.BlockerUserId == userId && b.BlockedUserId == peer, ct)
            || !await db.ConversationMembers.AnyAsync(m => m.ConversationId == id && m.UserId == peer && m.LeftAt == null, ct)
            || !await db.Friendships.AnyAsync(f => f.UserLowId == direct.UserLowId && f.UserHighId == direct.UserHighId
                && f.Status == "Accepted", ct))) throw Denied();
        return (false, member.JoinedSeq);
    }

    private static AttachmentServiceException Missing() => new(404, "ATTACHMENT_NOT_FOUND", "Attachment not found.");
    private static AttachmentServiceException Denied() => new(403, "CONVERSATION_ACCESS_DENIED", "Attachment access denied.");
    private static AttachmentServiceException InvalidFile() => new(400, "VALIDATION_ERROR", "One non-empty file with a valid filename is required.");
    private static AttachmentServiceException TooLarge() => new(413, "FILE_TOO_LARGE", "File must not exceed 10 MiB.");
}
