
using System.Data;
using Chat.Application.DTOs.Attachments;
using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.Services;

public sealed class AttachmentMessageService : IAttachmentMessageService
{
    private const int MaxFiles = 5;
    private const long MaxFileBytes = 10 * 1024 * 1024;
    private const long MaxTotalBytes = 50 * 1024 * 1024;
    private const int MaxBodyLength = 4000;

    private readonly ChatDbContext _db;
    private readonly IGroupPermissionService _groupPermissions;
    private readonly IConversationEventPublisher _eventPublisher;
    private readonly ILogger<AttachmentMessageService> _logger;

    public AttachmentMessageService(
        ChatDbContext db,
        IGroupPermissionService groupPermissions,
        IConversationEventPublisher eventPublisher,
        ILogger<AttachmentMessageService> logger)
    {
        _db = db;
        _groupPermissions = groupPermissions;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<SendAttachmentMessageResponse> SendAsync(
        Guid currentUserId,
        Guid conversationId,
        SendAttachmentMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (currentUserId == Guid.Empty ||
            conversationId == Guid.Empty ||
            request is null ||
            request.ClientMessageId == Guid.Empty ||
            request.Body is null ||
            request.Body.Length > MaxBodyLength ||
            request.AttachmentIds is null ||
            request.AttachmentIds.Count is < 1 or > MaxFiles ||
            request.AttachmentIds.Any(id => id == Guid.Empty) ||
            request.AttachmentIds.Distinct().Count() != request.AttachmentIds.Count)
        {
            throw Error(400, "VALIDATION_ERROR", "Invalid attachment message request.");
        }

        if (request.Body.Length == 0 && request.AttachmentIds.Count == 0)
            throw Error(400, "VALIDATION_ERROR", "Message body or attachment is required.");

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        // Serialize message creation in this conversation.
        var conversation = await _db.Conversations
            .FromSqlInterpolated(
                $"SELECT * FROM \"Conversations\" WHERE \"Id\" = {conversationId} FOR UPDATE")
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (conversation is null)
            throw Error(404, "NOT_FOUND", "Conversation not found.");

        var member = await EnsureAccessAsync(
            currentUserId, conversation, sending: true, cancellationToken);

        // Idempotency: the same client ID must have the same body and file IDs.
        var existing = await _db.Messages
            .AsNoTracking()
            .SingleOrDefaultAsync(m =>
                m.ConversationId == conversationId &&
                m.SenderUserId == currentUserId &&
                m.ClientMessageId == request.ClientMessageId,
                cancellationToken);

        if (existing is not null)
        {
            var existingIds = await _db.MessageAttachments
                .AsNoTracking()
                .Where(a => a.MessageId == existing.Id)
                .Select(a => a.StoredFileId)
                .OrderBy(id => id)
                .ToListAsync(cancellationToken);

            var requestedIds = request.AttachmentIds
                .OrderBy(id => id)
                .ToList();

            if (existing.Body != request.Body ||
                !existingIds.SequenceEqual(requestedIds))
            {
                throw Error(
                    409,
                    "CLIENT_MESSAGE_ID_CONFLICT",
                    "Client message ID was already used with different content.");
            }

            await transaction.CommitAsync(cancellationToken);

            return new SendAttachmentMessageResponse
            {
                Message = ToResponse(existing, false),
                Attachments = await GetAttachmentDtosAsync(
                    existing.Id, cancellationToken)
            };
        }

        // Lock files in a stable order to reduce concurrent-send conflicts.
        var files = new List<StoredFile>();

        foreach (var fileId in request.AttachmentIds.OrderBy(id => id))
        {
            var file = await _db.StoredFiles
                .FromSqlInterpolated(
                    $"SELECT * FROM \"StoredFiles\" WHERE \"Id\" = {fileId} FOR UPDATE")
                .AsTracking()
                .SingleOrDefaultAsync(cancellationToken);

            if (file is null)
                throw Error(404, "ATTACHMENT_NOT_FOUND", "Attachment not found.");

            if (file.UploadedByUserId != currentUserId ||
                file.ConversationId != conversationId)
            {
                throw AccessDenied();
            }

            var alreadyAttached = await _db.MessageAttachments
                .AnyAsync(a => a.StoredFileId == file.Id, cancellationToken);

            if (alreadyAttached)
            {
                throw Error(
                    409,
                    "ATTACHMENT_ALREADY_USED",
                    "An attachment is already linked to a message.");
            }

            if (file.UsageType != "Attachment" || file.Status != "Pending")
            {
                throw Error(
                    409,
                    "ATTACHMENT_NOT_AVAILABLE",
                    "Attachment is not available for this message.");
            }

            if (file.SizeBytes <= 0 || file.SizeBytes > MaxFileBytes)
                throw Error(400, "FILE_TOO_LARGE", "Each file must not exceed 10 MiB.");

            files.Add(file);
        }

        var totalBytes = files.Sum(f => f.SizeBytes);

        if (totalBytes > MaxTotalBytes)
            throw Error(400, "TOTAL_FILE_SIZE_EXCEEDED", "Total attachment size must not exceed 50 MiB.");

        var now = DateTimeOffset.UtcNow;
        conversation.LastMessageSeq++;
        conversation.LastActivityAt = now;

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            SenderUserId = currentUserId,
            ClientMessageId = request.ClientMessageId,
            Seq = conversation.LastMessageSeq,
            Body = request.Body,
            CreatedAt = now
        };

        _db.Messages.Add(message);

        foreach (var file in files)
        {
            file.Status = "Attached";

            _db.MessageAttachments.Add(new MessageAttachment
            {
                MessageId = message.Id,
                StoredFileId = file.Id
            });
        }

        // Message, links, file states and sequence are saved atomically.
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Notify only after the transaction has committed.
        try
        {
            await _eventPublisher.NotifyConversationChangedAsync(
                conversationId, message.Id, message.Seq);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish attachment message event for conversation {ConversationId}",
                conversationId);
        }

        return new SendAttachmentMessageResponse
        {
            Message = ToResponse(message, true),
            Attachments = files.Select(ToAttachmentDto).ToList()
        };
    }

    public async Task<IReadOnlyList<AttachmentMessageDto>> GetDetailsAsync(
        Guid currentUserId,
        Guid conversationId,
        IReadOnlyCollection<Guid> messageIds,
        CancellationToken cancellationToken = default)
    {
        if (currentUserId == Guid.Empty ||
            conversationId == Guid.Empty ||
            messageIds is null ||
            messageIds.Count is < 1 or > 100 ||
            messageIds.Any(id => id == Guid.Empty) ||
            messageIds.Distinct().Count() != messageIds.Count)
        {
            throw Error(400, "VALIDATION_ERROR", "Provide between 1 and 100 unique message IDs.");
        }

        var conversation = await _db.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null)
            throw Error(404, "NOT_FOUND", "Conversation not found.");

        var member = await EnsureAccessAsync(
            currentUserId, conversation, sending: false, cancellationToken);

        var ids = messageIds.ToArray();

        var visibleMessages = _db.Messages
            .AsNoTracking()
            .Where(m =>
                m.ConversationId == conversationId &&
                ids.Contains(m.Id) &&
                m.Seq >= member.JoinedSeq);

        if (conversation.Kind == "Group")
        {
            visibleMessages = visibleMessages.Where(m =>
                !_db.UserBlocks.Any(b =>
                    b.BlockerUserId == m.SenderUserId &&
                    b.BlockedUserId == currentUserId));
        }

        var visibleIds = await visibleMessages
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (visibleIds.Count != ids.Length)
        {
            throw AccessDenied();
        }

        var attachmentRows = await _db.MessageAttachments
            .AsNoTracking()
            .Include(a => a.StoredFile)
            .Where(a => visibleIds.Contains(a.MessageId))
            .ToListAsync(cancellationToken);

        var attachmentsByMessage = attachmentRows
            .GroupBy(a => a.MessageId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(a => ToAttachmentDto(a.StoredFile)).ToList());

        return visibleIds.Select(id => new AttachmentMessageDto
        {
            MessageId = id,
            Attachments = attachmentsByMessage.TryGetValue(id, out var list)
                ? list
                : []
        }).ToList();
    }

    private async Task<ConversationMember> EnsureAccessAsync(
        Guid userId,
        Conversation conversation,
        bool sending,
        CancellationToken ct)
    {
        var member = await _db.ConversationMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(m =>
                m.ConversationId == conversation.Id &&
                m.UserId == userId &&
                m.LeftAt == null,
                ct);

        if (member is null)
            throw AccessDenied();

        if (conversation.Kind == "Group")
        {
            var access = sending
                ? await _groupPermissions.CanSendMessageAsync(conversation.Id, userId, ct)
                : await _groupPermissions.CanViewMessagesAsync(conversation.Id, userId, ct);

            if (!access.Allowed)
            {
                throw Error(
                    access.HttpStatus ?? 403,
                    access.ErrorCode ?? "GROUP_ACCESS_DENIED",
                    "Group access denied.");
            }

            return member;
        }

        if (conversation.Kind != "Direct")
            throw AccessDenied();

        var direct = await _db.DirectConversations
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.ConversationId == conversation.Id, ct);

        if (direct is null ||
            (direct.UserLowId != userId && direct.UserHighId != userId))
            throw AccessDenied();

        var peerId = direct.UserLowId == userId
            ? direct.UserHighId
            : direct.UserLowId;

        // A user blocked by the peer cannot view or send.
        if (await _db.UserBlocks.AnyAsync(b =>
                b.BlockerUserId == peerId &&
                b.BlockedUserId == userId, ct))
            throw AccessDenied();

        if (!sending)
            return member;

        var blockedByCurrentUser = await _db.UserBlocks.AnyAsync(b =>
            b.BlockerUserId == userId &&
            b.BlockedUserId == peerId, ct);

        var peerIsMember = await _db.ConversationMembers.AnyAsync(m =>
            m.ConversationId == conversation.Id &&
            m.UserId == peerId &&
            m.LeftAt == null, ct);

        var areFriends = await _db.Friendships.AnyAsync(f =>
            f.UserLowId == direct.UserLowId &&
            f.UserHighId == direct.UserHighId &&
            f.Status == "Accepted", ct);

        if (blockedByCurrentUser || !peerIsMember || !areFriends)
            throw AccessDenied();

        return member;
    }

    private async Task<List<AttachmentDto>> GetAttachmentDtosAsync(
        Guid messageId,
        CancellationToken ct)
    {
        var files = await _db.MessageAttachments
            .AsNoTracking()
            .Include(a => a.StoredFile)
            .Where(a => a.MessageId == messageId)
            .Select(a => a.StoredFile)
            .ToListAsync(ct);

        return files.Select(ToAttachmentDto).ToList();
    }

    private static AttachmentDto ToAttachmentDto(StoredFile file)
    {
        return new AttachmentDto
        {
            Id = file.Id,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.SizeBytes,
            ContentUrl = $"/api/attachments/{file.Id}/content",
            DownloadUrl = $"/api/attachments/{file.Id}/download"
        };
    }

    private static MessageResponse ToResponse(Message message, bool isNew)
    {
        return new MessageResponse
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            SenderUserId = message.SenderUserId,
            ClientMessageId = message.ClientMessageId,
            Seq = message.Seq,
            Body = message.Body,
            CreatedAt = message.CreatedAt.ToUniversalTime(),
            IsNewlyCreated = isNew
        };
    }

    private static AttachmentServiceException AccessDenied()
    {
        return Error(403, "CONVERSATION_ACCESS_DENIED", "You do not have access to this conversation.");
    }

    private static AttachmentServiceException Error(
        int status,
        string code,
        string message)
    {
        return new AttachmentServiceException(status, code, message);
    }
}
