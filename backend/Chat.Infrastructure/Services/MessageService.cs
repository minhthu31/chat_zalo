using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Services
{
    public class MessageService : IMessageService
    {
        private readonly ChatDbContext _context;

        public MessageService(ChatDbContext context)
        {
            _context = context;
        }

        public async Task<(MessageResponse Data, int StatusCode)> SendAsync(Guid senderUserId, Guid conversationId, SendMessageRequest request)
        {
            var isMember = await _context.ConversationMembers
                .AnyAsync(m => m.ConversationId == conversationId && m.UserId == senderUserId);
            if (!isMember)
            {
                throw new UnauthorizedAccessException("Người dùng không thuộc hội thoại này.");
            }

            var otherMember = await _context.ConversationMembers
                .Where(m => m.ConversationId == conversationId && m.UserId != senderUserId)
                .Select(m => m.UserId)
                .FirstOrDefaultAsync();

            if (otherMember != default)
            {
                var isBlocked = await _context.UserBlocks
                    .AnyAsync(b => (b.BlockerUserId == senderUserId && b.BlockedUserId == otherMember) ||
                                   (b.BlockerUserId == otherMember && b.BlockedUserId == senderUserId));
                if (isBlocked)
                {
                    throw new InvalidOperationException("Không thể gửi tin nhắn do đã bị chặn.");
                }
            }

            var existingMessage = await _context.Messages
                .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.SenderUserId == senderUserId && m.ClientMessageId == request.ClientMessageId);

            if (existingMessage != null)
            {
                if (existingMessage.Body != request.Body)
                {
                    return (null, 409);
                }

                var existingResponse = new MessageResponse
                {
                    Id = existingMessage.Id,
                    ConversationId = existingMessage.ConversationId,
                    SenderUserId = existingMessage.SenderUserId,
                    Body = existingMessage.Body,
                    Seq = existingMessage.Seq,
                    ClientMessageId = existingMessage.ClientMessageId,
                    CreatedAt = existingMessage.CreatedAt
                };
                return (existingResponse, 200);
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var conversation = await _context.Conversations
                    .Where(c => c.Id == conversationId)
                    .FirstOrDefaultAsync();

                if (conversation == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy hội thoại.");
                }

                conversation.LastMessageSeq += 1;
                conversation.LastActivityAt = DateTimeOffset.UtcNow;

                var message = new Messages
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversationId,
                    SenderUserId = senderUserId,
                    Body = request.Body,
                    ClientMessageId = request.ClientMessageId,
                    Seq = conversation.LastMessageSeq,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _context.Messages.Add(message);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var response = new MessageResponse
                {
                    Id = message.Id,
                    ConversationId = message.ConversationId,
                    SenderUserId = message.SenderUserId,
                    Body = message.Body,
                    Seq = message.Seq,
                    ClientMessageId = message.ClientMessageId,
                    CreatedAt = message.CreatedAt
                };

                return (response, 201);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<IEnumerable<MessageResponse>> GetHistoryAsync(Guid userId, Guid conversationId, long? beforeSeq, long? afterSeq, int limit)
        {
            var isMember = await _context.ConversationMembers
                .AnyAsync(m => m.ConversationId == conversationId && m.UserId == userId);
            if (!isMember)
            {
                throw new UnauthorizedAccessException("Người dùng không có quyền truy cập lịch sử hội thoại này.");
            }

            var query = _context.Messages.Where(m => m.ConversationId == conversationId);

            if (beforeSeq.HasValue)
            {
                query = query.Where(m => m.Seq < beforeSeq.Value);
            }

            if (afterSeq.HasValue)
            {
                query = query.Where(m => m.Seq > afterSeq.Value);
            }

            return await query
                .OrderByDescending(m => m.Seq)
                .Take(limit > 0 ? limit : 20)
                .Select(m => new MessageResponse
                {
                    Id = m.Id,
                    ConversationId = m.ConversationId,
                    SenderUserId = m.SenderUserId,
                    Body = m.Body,
                    Seq = m.Seq,
                    ClientMessageId = m.ClientMessageId,
                    CreatedAt = m.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<IEnumerable<MessageResponse>> GetNewMessagesAsync(Guid userId, Guid conversationId, long lastSeq)
        {
            var isMember = await _context.ConversationMembers
                .AnyAsync(m => m.ConversationId == conversationId && m.UserId == userId);
            if (!isMember)
            {
                throw new UnauthorizedAccessException("Người dùng không có quyền truy cập.");
            }

            return await _context.Messages
                .Where(m => m.ConversationId == conversationId && m.Seq > lastSeq)
                .OrderBy(m => m.Seq)
                .Select(m => new MessageResponse
                {
                    Id = m.Id,
                    ConversationId = m.ConversationId,
                    SenderUserId = m.SenderUserId,
                    Body = m.Body,
                    Seq = m.Seq,
                    ClientMessageId = m.ClientMessageId,
                    CreatedAt = m.CreatedAt
                })
                .ToListAsync();
        }
    }
}