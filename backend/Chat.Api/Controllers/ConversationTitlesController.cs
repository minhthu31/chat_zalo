using System.Security.Claims;
using Chat.Application.Interfaces;
using Chat.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Chat.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/conversations/direct/titles")]
public class ConversationTitlesController(IConversationService conversations, ChatDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        // Reuse membership and directional block rules of the existing conversation list.
        var visible = (await conversations.GetConversationsAsync(userId))
            .Where(c => c.Kind == "Direct").Select(c => c.Id).ToArray();
        var titles = await (
            from direct in db.DirectConversations.AsNoTracking()
            join user in db.Users.AsNoTracking()
                on (direct.UserLowId == userId ? direct.UserHighId : direct.UserLowId) equals user.Id
            where visible.Contains(direct.ConversationId)
            select new
            {
                ConversationId = direct.ConversationId,
                UserId = user.Id,
                Username = user.UserName,
                DisplayName = user.DisplayName
            }).ToListAsync(ct);
        return Ok(titles);
    }
}
