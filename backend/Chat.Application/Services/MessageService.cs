using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Services;

public sealed class MessageService(ChatDbContext db, IFriendshipService friendships, IBlockService blocks) : IMessageService
{
    public async Task<MessageResponse> SendAsync(Guid currentUserId, Guid conversationId, SendMessageRequest request)
    {
        if (request.ClientMessageId == Guid.Empty || string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 4000)
            throw new MessageException(400, "VALIDATION_ERROR", "ClientMessageId and a body of 1–4000 characters are required.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        // Serialize allocation and idempotency checks across all API instances.
        var conversation = await db.Conversations.FromSqlInterpolated(
            $"SELECT * FROM \"Conversations\" WHERE \"Id\" = {conversationId} FOR UPDATE")
            .SingleOrDefaultAsync();
        if (conversation is null) throw new MessageException(404, "NOT_FOUND", "Conversation not found.");
        await db.Entry(conversation).ReloadAsync();
        var peers = await RequireAccess(currentUserId, conversationId);
        if (conversation.Kind != "Direct" || peers.Count != 1
            || !await friendships.AreFriendsAsync(currentUserId, peers[0])
            || await blocks.IsBlockedAsync(currentUserId, peers[0]))
            throw new MessageException(403, "CONVERSATION_ACCESS_DENIED", "Sending is not allowed.");

        var existing = await db.Messages.AsNoTracking().SingleOrDefaultAsync(m =>
            m.ConversationId == conversationId && m.SenderUserId == currentUserId
            && m.ClientMessageId == request.ClientMessageId);
        if (existing is not null)
        {
            if (existing.Body != request.Body)
                throw new MessageException(409, "CLIENT_MESSAGE_ID_CONFLICT", "Retry body differs from the original message.");
            await transaction.CommitAsync();
            return Map(existing);
        }

        var message = new Message
        {
            ConversationId = conversationId, SenderUserId = currentUserId,
            ClientMessageId = request.ClientMessageId, Body = request.Body,
            Seq = checked(conversation.LastMessageSeq + 1)
        };
        conversation.LastMessageSeq = message.Seq;
        conversation.LastActivityAt = message.CreatedAt;
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Map(message, true);
    }

    public async Task<List<MessageResponse>> GetHistoryAsync(Guid currentUserId, Guid conversationId, long? beforeSeq, int limit = 20)
    {
        ValidatePagination(beforeSeq, limit);
        await RequireAccess(currentUserId, conversationId);
        var messages = await db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId
            && (!beforeSeq.HasValue || m.Seq < beforeSeq.Value)).OrderByDescending(m => m.Seq).Take(limit).ToListAsync();
        return messages.OrderBy(m => m.Seq).Select(m => Map(m)).ToList();
    }

    public async Task<List<MessageResponse>> GetNewMessagesAsync(Guid currentUserId, Guid conversationId, long afterSeq, int limit = 20)
    {
        ValidatePagination(afterSeq, limit);
        await RequireAccess(currentUserId, conversationId);
        var messages = await db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId && m.Seq > afterSeq)
            .OrderBy(m => m.Seq).Take(limit).ToListAsync();
        return messages.Select(m => Map(m)).ToList();
    }

    private async Task<List<Guid>> RequireAccess(Guid userId, Guid conversationId)
    {
        if (!await db.ConversationMembers.AnyAsync(m => m.ConversationId == conversationId && m.UserId == userId && m.LeftAt == null))
            throw new MessageException(403, "CONVERSATION_ACCESS_DENIED", "Conversation access denied.");
        // Match notification authorization, including incoming blocks.
        if (await db.UserBlocks.AnyAsync(b => b.BlockedUserId == userId && db.ConversationMembers.Any(m =>
            m.ConversationId == conversationId && m.UserId == b.BlockerUserId && m.LeftAt == null)))
            throw new MessageException(403, "CONVERSATION_ACCESS_DENIED", "Conversation access denied.");
        return await db.ConversationMembers.Where(m => m.ConversationId == conversationId && m.UserId != userId && m.LeftAt == null)
            .Select(m => m.UserId).ToListAsync();
    }

    private static void ValidatePagination(long? seq, int limit)
    {
        if (seq < 0 || limit < 1 || limit > 100)
            throw new MessageException(400, "INVALID_PAGINATION_PARAMETERS", "Invalid seq or limit (1–100).");
    }

    private static MessageResponse Map(Message m, bool created = false) => new()
    {
        Id = m.Id, ConversationId = m.ConversationId, SenderUserId = m.SenderUserId,
        Seq = m.Seq, Body = m.Body, CreatedAt = m.CreatedAt, IsNewlyCreated = created
    };
}

public sealed class MessageException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
