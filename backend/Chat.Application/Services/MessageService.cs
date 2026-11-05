using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Chat.Application.DTOs.Messages;
using Chat.Application.Services;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Services;

public sealed class MessageService : IMessageService
{
    private readonly ChatDbContext _db;
    private readonly IConversationEventPublisher _eventPublisher;

    public MessageService(
        ChatDbContext db,
        IConversationEventPublisher eventPublisher)
    {
        _db = db;
        _eventPublisher = eventPublisher;
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
                "clientMessageId phải khác rỗng; body phải dài 1–4000 ký tự.");
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted);

        // Khóa hội thoại trước khi kiểm tra retry và cấp Seq.
        // Các request gửi trong cùng hội thoại sẽ xử lý lần lượt.
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
                404, "NOT_FOUND", "Không tìm thấy hội thoại.");
        }

        await EnsureAccessAsync(
            currentUserId, conversationId, forSending: true);

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
                    "ClientMessageId đã được sử dụng với nội dung khác.");
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
                .SetProperty(c => c.LastActivityAt, now));

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

        await _eventPublisher.NotifyConversationChangedAsync(
            conversationId,
            Guid.NewGuid(),
            message.Seq);

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

        await EnsureAccessAsync(
            currentUserId, conversationId, forSending: false);

        var query = _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId);

        if (beforeSeq.HasValue)
        {
            query = query.Where(m => m.Seq < beforeSeq.Value);
        }

        // Lấy trang gần nhất, sau đó trả theo Seq tăng dần.
        var messages = await query
            .OrderByDescending(m => m.Seq)
            .Take(limit)
            .ToListAsync();

        messages.Reverse();

        return messages.Select(m => ToResponse(m)).ToList();
    }

    public async Task<List<MessageResponse>> GetNewMessagesAsync(
        Guid currentUserId,
        Guid conversationId,
        long afterSeq,
        int limit = 20)
    {
        ValidateIds(currentUserId, conversationId);
        ValidatePagination(afterSeq, limit);

        await EnsureAccessAsync(
            currentUserId, conversationId, forSending: false);

        // Lấy những tin đầu tiên ngay sau cursor.
        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m =>
                m.ConversationId == conversationId &&
                m.Seq > afterSeq)
            .OrderBy(m => m.Seq)
            .Take(limit)
            .ToListAsync();

        return messages.Select(m => ToResponse(m)).ToList();
    }

    private async Task EnsureAccessAsync(
        Guid currentUserId,
        Guid conversationId,
        bool forSending)
    {
        var isMember = await _db.ConversationMembers
            .AsNoTracking()
            .AnyAsync(m =>
                m.ConversationId == conversationId &&
                m.UserId == currentUserId &&
                m.LeftAt == null);

        if (!isMember)
        {
            throw AccessDenied();
        }

        var direct = await _db.DirectConversations
            .AsNoTracking()
            .SingleOrDefaultAsync(d =>
                d.ConversationId == conversationId);

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
        var blockedByPeer = await _db.UserBlocks
            .AsNoTracking()
            .AnyAsync(b =>
                b.BlockerUserId == peerId &&
                b.BlockedUserId == currentUserId);

        if (blockedByPeer)
        {
            throw AccessDenied();
        }

        // Người chủ động chặn vẫn được xem lịch sử.
        if (!forSending)
        {
            return;
        }

        var blockedByCurrentUser = await _db.UserBlocks
            .AsNoTracking()
            .AnyAsync(b =>
                b.BlockerUserId == currentUserId &&
                b.BlockedUserId == peerId);

        var peerIsMember = await _db.ConversationMembers
            .AsNoTracking()
            .AnyAsync(m =>
                m.ConversationId == conversationId &&
                m.UserId == peerId &&
                m.LeftAt == null);

        var areFriends = await _db.Friendships
            .AsNoTracking()
            .AnyAsync(f =>
                f.UserLowId == direct.UserLowId &&
                f.UserHighId == direct.UserHighId &&
                f.Status == "Accepted");

        if (blockedByCurrentUser || !peerIsMember || !areFriends)
        {
            throw AccessDenied();
        }
    }

    private static void ValidateIds(
        Guid currentUserId,
        Guid conversationId)
    {
        if (currentUserId == Guid.Empty)
        {
            throw new MessageServiceException(
                401, "UNAUTHORIZED", "Bạn chưa đăng nhập.");
        }

        if (conversationId == Guid.Empty)
        {
            throw new MessageServiceException(
                400,
                "VALIDATION_ERROR",
                "conversationId phải khác rỗng.");
        }
    }

    private static void ValidatePagination(long? cursor, int limit)
    {
        if (cursor is < 0 || limit is < 1 or > 100)
        {
            throw new MessageServiceException(
                400,
                "INVALID_PAGINATION_PARAMETERS",
                "Mốc Seq phải không âm; limit phải từ 1 đến 100.");
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
