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
        private readonly ApplicationDbContext _context;

        public MessageService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<(MessageResponse Data, int StatusCode)> SendMessageAsync(Guid senderId, SendMessageRequest request)
        {
            var isMember = await _context.ConversationMembers
                .AnyAsync(m => m.ConversationId == request.ConversationId && m.UserId == senderId);
            if (!isMember)
            {
                throw new UnauthorizedAccessException("Người dùng không thuộc hội thoại này.");
            }

            var isBlocked = await _context.Blocks
                .AnyAsync(b => (b.BlockerId == senderId && b.BlockedId == request.ReceiverId) ||
                               (b.BlockerId == request.ReceiverId && b.BlockedId == senderId));
            if (isBlocked)
            {
                throw new InvalidOperationException("Không thể gửi tin nhắn do đã bị chặn.");
            }

            var existingMessage = await _context.Messages
                .FirstOrDefaultAsync(m => m.ClientMessageId == request.ClientMessageId);

            if (existingMessage != null)
            {
                var existingResponse = new MessageResponse
                {
                    Id = existingMessage.Id,
                    ConversationId = existingMessage.ConversationId,
                    SenderId = existingMessage.SenderId,
                    Content = existingMessage.Content,
                    Seq = existingMessage.Seq,
                    ClientMessageId = existingMessage.ClientMessageId,
                    CreatedAt = existingMessage.CreatedAt
                };
                return (existingResponse, 200);
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var maxSeq = await _context.Messages
                    .Where(m => m.ConversationId == request.ConversationId)
                    .MaxAsync(m => (long?)m.Seq) ?? 0;

                var message = new Message
                {
                    Id = Guid.NewGuid(),
                    ConversationId = request.ConversationId,
                    SenderId = senderId,
                    Content = request.Content,
                    ClientMessageId = request.ClientMessageId,
                    Seq = maxSeq + 1,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Messages.Add(message);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var response = new MessageResponse
                {
                    Id = message.Id,
                    ConversationId = message.ConversationId,
                    SenderId = message.SenderId,
                    Content = message.Content,
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

        public async Task<IEnumerable<MessageResponse>> GetMessageHistoryAsync(Guid userId, Guid conversationId, long? beforeSeq, long? afterSeq, int limit)
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
                    SenderId = m.SenderId,
                    Content = m.Content,
                    Seq = m.Seq,
                    ClientMessageId = m.ClientMessageId,
                    CreatedAt = m.CreatedAt
                })
                .ToListAsync();
        }
    }
}