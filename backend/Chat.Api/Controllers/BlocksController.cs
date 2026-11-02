
using System.Security.Claims;
using Chat.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;

namespace Chat.Api.Controllers;

[ApiController]
[Route("api/blocks")]
[Authorize]
public class BlocksController : ControllerBase
{
    private readonly IBlockService _blockService;
    private readonly IAntiforgery _antiforgery;

    public BlocksController(
        IBlockService blockService,
        IAntiforgery antiforgery)
    {
        _blockService = blockService;
        _antiforgery = antiforgery;
    }

    [HttpPut("{userId:guid}")]
    public async Task<IActionResult> BlockUser(Guid userId)
    {
        if (!await IsValidCsrfTokenAsync())
        {
            return BadRequest(new
            {
                code = "INVALID_CSRF_TOKEN",
                message = "CSRF token is missing or invalid."
            });
        }

        var currentUserId = GetCurrentUserId();

        if (currentUserId == null)
        {
            return Unauthorized(new
            {
                code = "UNAUTHORIZED",
                message = "Authentication is required."
            });
        }

        try
        {
            await _blockService.BlockAsync(
                currentUserId.Value, userId);

            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new
            {
                code = "SELF_BLOCK_NOT_ALLOWED",
                message = "You cannot block yourself."
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                code = "NOT_FOUND",
                message = "User not found."
            });
        }
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> UnblockUser(Guid userId)
    {
        if (!await IsValidCsrfTokenAsync())
        {
            return BadRequest(new
            {
                code = "INVALID_CSRF_TOKEN",
                message = "CSRF token is missing or invalid."
            });
        }

        var currentUserId = GetCurrentUserId();

        if (currentUserId == null)
        {
            return Unauthorized(new
            {
                code = "UNAUTHORIZED",
                message = "Authentication is required."
            });
        }

        await _blockService.UnblockAsync(
            currentUserId.Value, userId);

        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        return Guid.TryParse(userId, out var id)
            ? id
            : null;
    }

    private async Task<bool> IsValidCsrfTokenAsync()
    {
        try
        {
            await _antiforgery.ValidateRequestAsync(HttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
