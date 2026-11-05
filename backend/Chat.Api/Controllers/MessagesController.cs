using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Chat.Application.DTOs.Messages;
using Chat.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MessagesController : ControllerBase
    {
        private readonly IMessageService _messageService;

        public MessagesController(IMessageService messageService)
        {
            _messageService = messageService;
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            var userIdClaim = User.FindFirst("sub") ?? User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var senderId))
            {
                return Unauthorized(new { message = "Token không hợp lệ hoặc hết hạn." });
            }

            try
            {
                var (data, statusCode) = await _messageService.SendMessageAsync(senderId, request);
                return StatusCode(statusCode, data);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("conversation/{conversationId}")]
        public async Task<IActionResult> GetHistory(
            Guid conversationId, 
            [FromQuery] long? beforeSeq, 
            [FromQuery] long? afterSeq, 
            [FromQuery] int limit = 20)
        {
            var userIdClaim = User.FindFirst("sub") ?? User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return Unauthorized(new { message = "Token không hợp lệ hoặc hết hạn." });
            }

            try
            {
                var result = await _messageService.GetMessageHistoryAsync(userId, conversationId, beforeSeq, afterSeq, limit);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
        }
    }
}