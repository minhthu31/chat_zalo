using System.Security.Claims;
using Chat.Api.Filters;
using Chat.Application.DTOs.Friendships;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

[ApiController]
[Authorize]
[ValidateAntiforgeryJson]
[Route("api/friendships")]
public sealed class FriendshipsController(IFriendshipService friendships) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SendRequest([FromBody] FriendRequestDto? request)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized(Error("UNAUTHORIZED", "Authentication is required."));
        if (request is null)
            return BadRequest(Error("VALIDATION_ERROR", "Request body is required."));
        try
        {
            var result = await friendships.SendRequestAsync(currentUserId, request.TargetUserId);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (FriendshipException ex) { return FriendshipError(ex); }
    }

    [HttpGet]
    public async Task<IActionResult> GetFriends()
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized(Error("UNAUTHORIZED", "Authentication is required."));
        try { return Ok(await friendships.GetFriendsAsync(currentUserId)); }
        catch (FriendshipException ex) { return FriendshipError(ex); }
    }

    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests()
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized(Error("UNAUTHORIZED", "Authentication is required."));
        try { return Ok(await friendships.GetRequestsAsync(currentUserId)); }
        catch (FriendshipException ex) { return FriendshipError(ex); }
    }

    [HttpPost("{userId}/accept")]
    public Task<IActionResult> Accept(Guid userId) => ExecuteNoContentAsync(userId, friendships.AcceptAsync);

    [HttpPost("{userId}/reject")]
    public Task<IActionResult> Reject(Guid userId) => ExecuteNoContentAsync(userId, friendships.RejectAsync);

    [HttpDelete("{userId}")]
    public Task<IActionResult> Remove(Guid userId) => ExecuteNoContentAsync(userId, friendships.RemoveAsync);

    private async Task<IActionResult> ExecuteNoContentAsync(Guid otherUserId, Func<Guid, Guid, Task> action)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized(Error("UNAUTHORIZED", "Authentication is required."));
        try
        {
            await action(currentUserId, otherUserId);
            return NoContent();
        }
        catch (FriendshipException ex) { return FriendshipError(ex); }
    }

    private bool TryGetCurrentUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private ObjectResult FriendshipError(FriendshipException ex) =>
        StatusCode(ex.StatusCode, Error(ex.Code, ex.Message));

    private static object Error(string code, string message) => new { code, message };
}
