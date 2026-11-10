using System.Data;
using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.Services;

public sealed class MessageService : IMessageService
{
    private readonly ChatDbContext _db;
    private readonly IBlockService _blockService;
    private readonly IConversationEventPublisher _eventPublisher;
    private readonly ILogger<MessageService> _logger;

    public MessageService(
        ChatDbContext db,
        IBlockService blockService,
        IConversationEventPublisher eventPublisher,
        ILogger<MessageService> logger)
    {
        _db = db;
        _blockService = blockService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<MessageResponse> SendAsync(
        Guid currentUserId,
        Guid conversationId,
        SendMessageRequest request)
    {
        ValidateIds(currentUserId, conversationId);

        if (request is null ||
            request.ClientMessageId == Guid.Empty ||
            request.Body is null ||
            request.Body.Length is < 1 or > 4000)
        {
            throw new MessageServiceException(
                400,
                "VALIDATION_ERROR",
                "clientMessageId không được rỗng; body phải từ 1 đến 4000 ký tự.");
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted);

        // Khóa hội thoại để tránh cấp trùng Seq khi gửi đồng thời.
        var conversations = await _db.Conversations
            .FromSqlInterpolated($"""
                SELECT *
                FROM "Conversations"
                WHERE "Id" = {conversationId}
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync();

        if (conversations.Count == 0)
        {
            throw new MessageServiceException(
                404,
                "NOT_FOUND",
                "conversation not found");
        }

        var conversation = conversations[0];

        await EnsureAccessAsync(
            currentUserId,
            conversation,
            forSending: true);

        // Chống gửi trùng khi client retry.
        var existing = await _db.Messages
            .AsNoTracking()
            .SingleOrDefaultAsync(m =>
                m.ConversationId == conversationId &&
                m.SenderUserId == currentUserId &&
                m.ClientMessageId == request.ClientMessageId);

        if (existing is not null)
        {
            if (!string.Equals(
                existing.Body,
                request.Body,
                StringComparison.Ordinal))
            {
                throw new MessageServiceException(
                    409,
                    "CLIENT_MESSAGE_ID_CONFLICT",
                    "clientMessageId đã được sử dụng cho nội dung khác.");
            }

            await transaction.CommitAsync();
            return ToResponse(existing);
        }

        var now = DateTimeOffset.UtcNow;

        await _db.Conversations
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    c => c.LastMessageSeq,
                    c => c.LastMessageSeq + 1)
                .SetProperty(
                    c => c.LastActivityAt,
                    now));

        var seq = await _db.Conversations
            .AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => c.LastMessageSeq)
            .SingleAsync();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            SenderUserId = currentUserId,
            ClientMessageId = request.ClientMessageId,
            Body = request.Body,
            Seq = seq,
            CreatedAt = now
        };

        _db.Messages.Add(message);

        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            _db.Entry(message).State = EntityState.Detached;
            throw;
        }

        // Chỉ gửi thông báo sau khi dữ liệu đã commit.
        var eventId = Guid.NewGuid();

        try
        {
            await _eventPublisher.NotifyConversationChangedAsync(
                conversationId,
                eventId,
                message.Seq);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to notify conversation {ConversationId}",
                conversationId);
        }

        return ToResponse(message, isNewlyCreated: true);
    }

    public async Task<List<MessageResponse>> GetHistoryAsync(
        Guid currentUserId,
        Guid conversationId,
        long? beforeSeq,
        int limit = 20)
    {
        ValidateIds(currentUserId, conversationId);
        ValidatePagination(beforeSeq, limit);

        var member = await GetActiveMemberAsync(
            currentUserId,
            conversationId);

        var conversation = await GetConversationAsync(conversationId);

        await EnsureAccessAsync(
            currentUserId,
            conversation,
            forSending: false);

        var query = _db.Messages
            .AsNoTracking()
            .Where(m =>
                m.ConversationId == conversationId &&
                m.Seq >= member.JoinedSeq);

        // CHAT-16: lọc tin nhắn của người đã chặn mình trong nhóm.
        // Phải lọc trước OrderBy và Take để trang có đủ tin nhắn.
        if (conversation.Kind == "Group")
        {
            query = query.Where(m =>
                !_db.UserBlocks.Any(b =>
                    b.BlockerUserId == m.SenderUserId &&
                    b.BlockedUserId == currentUserId));
        }

        if (beforeSeq.HasValue)
        {
            query = query.Where(m => m.Seq < beforeSeq.Value);
        }

        var messages = await query
            .OrderByDescending(m => m.Seq)
            .Take(limit)
            .ToListAsync();

        messages.Reverse();

        return messages
            .Select(m => ToResponse(m))
            .ToList();
    }

    public async Task<List<MessageResponse>> GetNewMessagesAsync(
        Guid currentUserId,
        Guid conversationId,
        long afterSeq,
        int limit = 20)
    {
        ValidateIds(currentUserId, conversationId);
        ValidatePagination(afterSeq, limit);

        var member = await GetActiveMemberAsync(
            currentUserId,
            conversationId);

        var conversation = await GetConversationAsync(conversationId);

        await EnsureAccessAsync(
            currentUserId,
            conversation,
            forSending: false);

        var query = _db.Messages
            .AsNoTracking()
            .Where(m =>
                m.ConversationId == conversationId &&
                m.Seq > afterSeq &&
                m.Seq >= member.JoinedSeq);

        // CHAT-16: chỉ lọc tin nhắn bị chặn trong Group.
        if (conversation.Kind == "Group")
        {
            query = query.Where(m =>
                !_db.UserBlocks.Any(b =>
                    b.BlockerUserId == m.SenderUserId &&
                    b.BlockedUserId == currentUserId));
        }

        var messages = await query
            .OrderBy(m => m.Seq)
            .Take(limit)
            .ToListAsync();

        return messages
            .Select(m => ToResponse(m))
            .ToList();
    }

    private async Task EnsureAccessAsync(
        Guid currentUserId,
        Conversation conversation,
        bool forSending)
    {
        // Thành viên đang hoạt động mới được truy cập.
        var isMember = await _db.ConversationMembers
            .AsNoTracking()
            .AnyAsync(m =>
                m.ConversationId == conversation.Id &&
                m.UserId == currentUserId &&
                m.LeftAt == null);

        if (!isMember)
        {
            throw AccessDenied();
        }

        // CHAT-16: nhóm không áp dụng quy tắc chặn của chat 1-1.
        if (conversation.Kind == "Group")
        {
            if (forSending)
            {
                var groupIsActive = await _db.GroupConversations
                    .AsNoTracking()
                    .AnyAsync(g =>
                        g.ConversationId == conversation.Id &&
                        g.DeletedAt == null);

                if (!groupIsActive)
                {
                    throw AccessDenied();
                }
            }

            return;
        }

        if (conversation.Kind != "Direct")
        {
            throw AccessDenied();
        }

        var direct = await _db.DirectConversations
            .AsNoTracking()
            .SingleOrDefaultAsync(d =>
                d.ConversationId == conversation.Id);

        if (direct is null ||
            (direct.UserLowId != currentUserId &&
             direct.UserHighId != currentUserId))
        {
            throw AccessDenied();
        }

        var peerId = direct.UserLowId == currentUserId
            ? direct.UserHighId
            : direct.UserLowId;

        // Người bị đối phương chặn không được đọc hoặc gửi.
        var blockedByPeer = await _blockService.HasBlockedAsync(
            peerId,
            currentUserId);

        if (blockedByPeer)
        {
            throw AccessDenied();
        }

        // Người chủ động chặn vẫn được xem lịch sử.
        if (!forSending)
        {
            return;
        }

        var blockedByCurrentUser =
            await _blockService.HasBlockedAsync(
                currentUserId,
                peerId);

        var peerIsMember = await _db.ConversationMembers
            .AsNoTracking()
            .AnyAsync(m =>
                m.ConversationId == conversation.Id &&
                m.UserId == peerId &&
                m.LeftAt == null);

        var areFriends = await _db.Friendships
            .AsNoTracking()
            .AnyAsync(f =>
                f.UserLowId == direct.UserLowId &&
                f.UserHighId == direct.UserHighId &&
                f.Status == "Accepted");

        if (blockedByCurrentUser ||
            !peerIsMember ||
            !areFriends)
        {
            throw AccessDenied();
        }
    }

    private async Task<ConversationMember> GetActiveMemberAsync(
        Guid userId,
        Guid conversationId)
    {
        var member = await _db.ConversationMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.UserId == userId &&
                m.ConversationId == conversationId &&
                m.LeftAt == null);

        return member ?? throw AccessDenied();
    }

    private async Task<Conversation> GetConversationAsync(
        Guid conversationId)
    {
        return await _db.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId)
            ?? throw new MessageServiceException(
                404,
                "NOT_FOUND",
                "conversation not found");
    }

    private static void ValidateIds(
        Guid currentUserId,
        Guid conversationId)
    {
        if (currentUserId == Guid.Empty)
        {
            throw new MessageServiceException(
                401,
                "UNAUTHORIZED",
                "Bạn chưa đăng nhập.");
        }

        if (conversationId == Guid.Empty)
        {
            throw new MessageServiceException(
                400,
                "VALIDATION_ERROR",
                "conversationId phải khác rỗng.");
        }
    }

    private static void ValidatePagination(
        long? cursor,
        int limit)
    {
        if (cursor is < 0 || limit is < 1 or > 100)
        {
            throw new MessageServiceException(
                400,
                "INVALID_PAGINATION_PARAMETERS",
                "Seq phải không âm; limit phải từ 1 đến 100.");
        }
    }

    private static MessageServiceException AccessDenied()
    {
        return new MessageServiceException(
            403,
            "CONVERSATION_ACCESS_DENIED",
            "Bạn không có quyền thực hiện thao tác trong hội thoại này.");
    }

    private static MessageResponse ToResponse(
        Message message,
        bool isNewlyCreated = false)
    {
        return new MessageResponse
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            SenderUserId = message.SenderUserId,
            ClientMessageId = message.ClientMessageId,
            Body = message.Body,
            Seq = message.Seq,
            CreatedAt = message.CreatedAt.ToUniversalTime(),
            IsNewlyCreated = isNewlyCreated
        };
    }
}