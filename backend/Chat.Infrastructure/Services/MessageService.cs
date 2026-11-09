using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Services;

public class MessageService : IMessageService
{
    private readonly ChatDbContext _context;
    private readonly IBlockService _blockService;

    public MessageService(
        ChatDbContext context,
        IBlockService blockService)
    {
        _context = context;
        _blockService = blockService;
    }

    public async Task<MessageResponse> SendAsync(
        Guid currentUserId,
        Guid conversationId,
        SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body) ||
            request.Body.Length > 4000)
        {
            throw new InvalidOperationException("INVALID_MESSAGE_BODY");
        }

        if (request.ClientMessageId == Guid.Empty)
        {
            throw new InvalidOperationException("INVALID_CLIENT_MESSAGE_ID");
        }

        var member = await _context.ConversationMembers
            .FirstOrDefaultAsync(m =>
                m.ConversationId == conversationId &&
                m.UserId == currentUserId &&
                m.LeftAt == null);

        if (member == null)
            throw new UnauthorizedAccessException("CONVERSATION_ACCESS_DENIED");

        var conversation = await _context.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conversation == null)
            throw new KeyNotFoundException("CONVERSATION_NOT_FOUND");

        // Direct: giữ nguyên quy tắc chặn hai chiều.
        // Group: người bị chặn vẫn được gửi tin nhắn.
        if (conversation.Kind == "Direct")
        {
            var direct = await _context.DirectConversations
                .FirstOrDefaultAsync(d => d.ConversationId == conversationId);

            if (direct == null)
                throw new KeyNotFoundException("DIRECT_CONVERSATION_NOT_FOUND");

            if (await _blockService.IsBlockedAsync(
                direct.UserLowId,
                direct.UserHighId))
            {
                throw new InvalidOperationException("USER_BLOCKED");
            }
        }

        // Chống gửi trùng khi client retry.
        var existingMessage = await _context.Messages
            .FirstOrDefaultAsync(m =>
                m.ConversationId == conversationId &&
                m.SenderUserId == currentUserId &&
                m.ClientMessageId == request.ClientMessageId);

        if (existingMessage != null)
            return ToResponse(existingMessage);

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        // Tăng Seq nguyên tử trong database.
        var updated = await _context.Conversations
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    c => c.LastMessageSeq,
                    c => c.LastMessageSeq + 1)
                .SetProperty(
                    c => c.LastActivityAt,
                    DateTimeOffset.UtcNow));

        if (updated == 0)
            throw new KeyNotFoundException("CONVERSATION_NOT_FOUND");

        var nextSeq = await _context.Conversations
            .Where(c => c.Id == conversationId)
            .Select(c => c.LastMessageSeq)
            .SingleAsync();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            SenderUserId = currentUserId,
            ClientMessageId = request.ClientMessageId,
            Seq = nextSeq,
            Body = request.Body,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Messages.Add(message);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return ToResponse(message);
    }

    public async Task<List<MessageResponse>> GetHistoryAsync(
        Guid currentUserId,
        Guid conversationId,
        long? beforeSeq,
        int limit = 20)
    {
        ValidateLimit(limit);

        var member = await GetActiveMemberAsync(
            currentUserId, conversationId);

        var conversation = await GetConversationAsync(conversationId);

        // Người bị chặn trong Direct không được xem lịch sử.
        if (conversation.Kind == "Direct")
        {
            var direct = await _context.DirectConversations
                .FirstAsync(d => d.ConversationId == conversationId);

            if (await _blockService.HasBlockedAsync(
                direct.UserLowId == currentUserId
                    ? direct.UserHighId
                    : direct.UserLowId,
                currentUserId))
            {
                throw new UnauthorizedAccessException(
                    "CONVERSATION_ACCESS_DENIED");
            }
        }

        var query = _context.Messages
            .Where(m =>
                m.ConversationId == conversationId &&
                m.Seq >= member.JoinedSeq);

        // Chỉ lọc tin nhắn bị chặn trong Group.
        // Lọc trước OrderBy và Take để lấy đủ số tin nhắn hiển thị.
        if (conversation.Kind == "Group")
        {
            query = query.Where(m =>
                !_context.UserBlocks.Any(b =>
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

        return messages
            .OrderBy(m => m.Seq)
            .Select(ToResponse)
            .ToList();
    }

    public async Task<List<MessageResponse>> GetNewMessagesAsync(
        Guid currentUserId,
        Guid conversationId,
        long afterSeq,
        int limit = 20)
    {
        ValidateLimit(limit);

        var member = await GetActiveMemberAsync(
            currentUserId, conversationId);

        var conversation = await GetConversationAsync(conversationId);

        if (conversation.Kind == "Direct")
        {
            var direct = await _context.DirectConversations
                .FirstAsync(d => d.ConversationId == conversationId);

            if (await _blockService.HasBlockedAsync(
                direct.UserLowId == currentUserId
                    ? direct.UserHighId
                    : direct.UserLowId,
                currentUserId))
            {
                throw new UnauthorizedAccessException(
                    "CONVERSATION_ACCESS_DENIED");
            }
        }

        var query = _context.Messages
            .Where(m =>
                m.ConversationId == conversationId &&
                m.Seq > afterSeq &&
                m.Seq >= member.JoinedSeq);

        if (conversation.Kind == "Group")
        {
            query = query.Where(m =>
                !_context.UserBlocks.Any(b =>
                    b.BlockerUserId == m.SenderUserId &&
                    b.BlockedUserId == currentUserId));
        }

        var messages = await query
            .OrderBy(m => m.Seq)
            .Take(limit)
            .ToListAsync();

        return messages.Select(ToResponse).ToList();
    }

    private async Task<ConversationMember> GetActiveMemberAsync(
        Guid userId,
        Guid conversationId)
    {
        var member = await _context.ConversationMembers
            .FirstOrDefaultAsync(m =>
                m.UserId == userId &&
                m.ConversationId == conversationId &&
                m.LeftAt == null);

        return member ??
            throw new UnauthorizedAccessException(
                "CONVERSATION_ACCESS_DENIED");
    }

    private async Task<Conversation> GetConversationAsync(
        Guid conversationId)
    {
        return await _context.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId)
            ?? throw new KeyNotFoundException("CONVERSATION_NOT_FOUND");
    }

    private static void ValidateLimit(int limit)
    {
        if (limit < 1 || limit > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));
    }

    private static MessageResponse ToResponse(Message message)
    {
        return new MessageResponse
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            SenderUserId = message.SenderUserId,
            ClientMessageId = message.ClientMessageId,
            Seq = message.Seq,
            Body = message.Body,
            CreatedAt = message.CreatedAt
        };
    }
}