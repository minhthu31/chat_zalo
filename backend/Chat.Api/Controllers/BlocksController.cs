using System.Security.Claims;
using Chat.Api.Filters;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

[ApiController]
[Authorize]
[ValidateAntiforgeryJson]
[Route("api/blocks")]
public sealed class BlocksController(IBlockService blocks) : ControllerBase
{
    [HttpPut("{userId}")]
    public Task<IActionResult> Block(Guid userId) => ExecuteAsync(userId, blocks.BlockAsync);

    [HttpDelete("{userId}")]
    public Task<IActionResult> Unblock(Guid userId) => ExecuteAsync(userId, blocks.UnblockAsync);

    private async Task<IActionResult> ExecuteAsync(Guid targetUserId, Func<Guid, Guid, Task> action)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId))
            return Unauthorized(new { code = "UNAUTHORIZED", message = "Authentication is required." });

        try
        {
            await action(currentUserId, targetUserId);
            return NoContent();
        }
        catch (BlockException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
    }
}
