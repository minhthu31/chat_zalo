using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/conversations/{id}/messages")]
    public class MessagesController : ControllerBase
    {
        private readonly IMessageService _messageService;

        public MessagesController(IMessageService messageService)
        {
            _messageService = messageService;
        }

        private Guid GetCurrentUserId()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return Guid.TryParse(userIdStr, out var userId) ? userId : throw new UnauthorizedAccessException("Không xác định được người dùng.");
        }

        [HttpPost]
        public async Task<ActionResult<MessageResponse>> SendMessage(Guid id, [FromBody] SendMessageRequest request)
        {
            var userId = GetCurrentUserId();
            var (data, statusCode) = await _messageService.SendAsync(userId, id, request);

            if (statusCode == 409)
            {
                return Conflict(new { message = "ClientMessageId đã tồn tại nhưng nội dung không khớp." });
            }

            if (statusCode == 201)
            {
                return StatusCode(201, data);
            }

            return Ok(data);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<MessageResponse>>> GetHistory(
            Guid id,
            [FromQuery] long? beforeSeq,
            [FromQuery] long? afterSeq,
            [FromQuery] int limit = 20)
        {
            var userId = GetCurrentUserId();
            var history = await _messageService.GetHistoryAsync(userId, id, beforeSeq, afterSeq, limit);
            return Ok(history);
        }
    }
}