<<<<<<< HEAD
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
=======
>>>>>>> f50182b (Implement CHAT-16 group message visibility)
using Chat.Application.DTOs.Messages;

namespace Chat.Application.Interfaces;

public interface IMessageService
{
    Task<MessageResponse> SendAsync(
        Guid currentUserId,
        Guid conversationId,
        SendMessageRequest request);

    Task<List<MessageResponse>> GetHistoryAsync(
        Guid currentUserId,
        Guid conversationId,
        long? beforeSeq,
        int limit = 20);

    Task<List<MessageResponse>> GetNewMessagesAsync(
        Guid currentUserId,
        Guid conversationId,
        long afterSeq,
        int limit = 20);
}