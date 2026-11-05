using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chat.Application.DTOs.Messages;

namespace Chat.Application.Interfaces
{
    public interface IMessageService
    {
        Task<(MessageResponse Data, int StatusCode)> SendMessageAsync(Guid senderId, SendMessageRequest request);
        Task<IEnumerable<MessageResponse>> GetMessageHistoryAsync(Guid userId, Guid conversationId, long? beforeSeq, long? afterSeq, int limit);
    }
}