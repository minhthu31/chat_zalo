
using Chat.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

[ApiController]
[Route("api/blocks")]
public class BlocksController : ControllerBase
{
    private readonly IBlockService _blockService;

    public BlocksController(IBlockService blockService)
    {
        _blockService = blockService;
    }

    [HttpPut("{userId:guid}")]
    public async Task<IActionResult> BlockUser(Guid userId)
    {
        var blockerUserId = GetCurrentUserId();

        if (blockerUserId == null)
            return Unauthorized();

        try
        {
            await _blockService.BlockUserAsync(blockerUserId.Value, userId);
            return Ok(new { message = "User blocked successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> UnblockUser(Guid userId)
    {
        var blockerUserId = GetCurrentUserId();

        if (blockerUserId == null)
            return Unauthorized();

        await _blockService.UnblockUserAsync(blockerUserId.Value, userId);

        return Ok(new { message = "User unblocked successfully." });
    }

    private Guid? GetCurrentUserId()
    {
        var userId = User.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(userId, out var id) ? id : null;
    }
}
