using System.Security.Claims;
using Chat.Api.Filters;
using Chat.Application.DTOs.Auth;
using Chat.Application.DTOs.Friendships;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

/// <summary>HTTP endpoints for sending and managing friendship requests.</summary>
[ApiController]
[Authorize]
[Route("api/friendships")]
[ValidateAntiforgeryJson]
public sealed class FriendshipsController : ControllerBase
{
    private readonly IFriendshipService _friendships;
    private readonly IFriendshipQueryService _friendshipQueries;

    public FriendshipsController(
        IFriendshipService friendships,
        IFriendshipQueryService friendshipQueries)
    {
        _friendships = friendships;
        _friendshipQueries = friendshipQueries;
    }

    /// <summary>Send a friend request to targetUserId. Returns 201 + FriendshipDto.</summary>
    [HttpPost]
    public async Task<IActionResult> SendRequest([FromBody] FriendRequestDto? request)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthenticated();

        try
        {
            if (request is null)
                throw FriendshipException.Validation("Dữ liệu gửi lời mời không được để trống.");

            var result = await _friendships.SendRequestAsync(currentUserId, request.TargetUserId);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (FriendshipException exception)
        {
            return Error(exception);
        }
    }

    /// <summary>Returns only accepted friendships.</summary>
    [HttpGet]
    public async Task<IActionResult> GetFriends()
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthenticated();

        try
        {
            return Ok(await _friendshipQueries.GetFriendsAsync(currentUserId));
        }
        catch (FriendshipException exception)
        {
            return Error(exception);
        }
    }

    /// <summary>Returns pending invitations grouped into incoming and outgoing.</summary>
    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests()
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthenticated();

        try
        {
            return Ok(await _friendshipQueries.GetRequestsAsync(currentUserId));
        }
        catch (FriendshipException exception)
        {
            return Error(exception);
        }
    }

    /// <summary>Only the recipient of the request may accept it.</summary>
    [HttpPost("{userId}/accept")]
    public async Task<IActionResult> Accept(Guid userId)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthenticated();

        try
        {
            await _friendships.AcceptAsync(currentUserId, userId);
            return NoContent();
        }
        catch (FriendshipException exception)
        {
            return Error(exception);
        }
    }

    /// <summary>Only the recipient of the request may reject it.</summary>
    [HttpPost("{userId}/reject")]
    public async Task<IActionResult> Reject(Guid userId)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthenticated();

        try
        {
            await _friendships.RejectAsync(currentUserId, userId);
            return NoContent();
        }
        catch (FriendshipException exception)
        {
            return Error(exception);
        }
    }

    /// <summary>The requester cancels a pending request; either friend may unfriend.</summary>
    [HttpDelete("{userId}")]
    public async Task<IActionResult> Remove(Guid userId)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthenticated();

        try
        {
            await _friendships.RemoveAsync(currentUserId, userId);
            return NoContent();
        }
        catch (FriendshipException exception)
        {
            return Error(exception);
        }
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out userId) && userId != Guid.Empty;
    }

    private IActionResult Error(FriendshipException exception) =>
        StatusCode(exception.StatusCode, new AuthErrorResponse
        {
            Code = exception.Code,
            Message = exception.Message
        });

    private IActionResult Unauthenticated() =>
        StatusCode(StatusCodes.Status401Unauthorized, new AuthErrorResponse
        {
            Code = "UNAUTHORIZED",
            Message = "Bạn chưa đăng nhập."
        });
}
