using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chat.Application.DTOs.Messages;

namespace Chat.Application.Interfaces
{
    public interface IMessageService
    {
        Task<(MessageResponse Data, int StatusCode)> SendAsync(Guid senderUserId, Guid conversationId, SendMessageRequest request);
        Task<IEnumerable<MessageResponse>> GetHistoryAsync(Guid userId, Guid conversationId, long? beforeSeq, long? afterSeq, int limit);
        Task<IEnumerable<MessageResponse>> GetNewMessagesAsync(Guid userId, Guid conversationId, long lastSeq);
    }
}