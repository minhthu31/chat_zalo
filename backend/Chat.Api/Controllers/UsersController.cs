
using System.Security.Claims;
using Chat.Application.DTOs.Users;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Chat.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(UserManager<ApplicationUser> userManager)
    {
        _userService = new UserService(
            new IdentityUserAccountGateway(userManager));
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new
            {
                errorCode = "UNAUTHORIZED",
                message = "User is not authenticated."
            });
        }

        var user = await _userService.GetCurrentUserAsync(userId);

        if (user is null)
        {
            return NotFound(new
            {
                errorCode = "USER_NOT_FOUND",
                message = "User not found."
            });
        }

        return Ok(user);
    }

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request)
    {
        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new
            {
                errorCode = "UNAUTHORIZED",
                message = "User is not authenticated."
            });
        }

        try
        {
            var user = await _userService.UpdateProfileAsync(
                userId, request);

            if (user is null)
            {
                return NotFound(new
                {
                    errorCode = "USER_NOT_FOUND",
                    message = "User not found."
                });
            }

            return Ok(user);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                errorCode = "INVALID_DISPLAY_NAME",
                message = ex.Message
            });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    errorCode = "PROFILE_UPDATE_FAILED",
                    message = "Could not update profile."
                });
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers(
        [FromQuery] string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return BadRequest(new
            {
                errorCode = "USERNAME_REQUIRED",
                message = "Username is required."
            });
        }

        try
        {
            var users = await _userService.SearchUsersAsync(username);
            return Ok(users);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                errorCode = "INVALID_USERNAME",
                message = ex.Message
            });
        }
    }
}

internal sealed class IdentityUserAccountGateway
    : IUserAccountGateway
{
    private readonly UserManager<ApplicationUser> _users;

    public IdentityUserAccountGateway(
        UserManager<ApplicationUser> users)
    {
        _users = users;
    }

    public async Task<UserDto?> FindByIdAsync(string userId)
    {
        var user = await _users.FindByIdAsync(userId);

        return user is null ? null : ToDto(user);
    }

    public async Task<UserDto?> UpdateDisplayNameAsync(
        string userId,
        string displayName)
    {
        var user = await _users.FindByIdAsync(userId);

        if (user is null)
            return null;

        user.DisplayName = displayName;

        var result = await _users.UpdateAsync(user);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Identity rejected the profile update.");
        }

        return ToDto(user);
    }

    public async Task<IReadOnlyList<UserDto>> SearchByUsernameAsync(
        string username)
    {
        var pattern = $"%{EscapeLikePattern(username)}%";

        return await _users.Users
            .AsNoTracking()
            .Where(user =>
                user.UserName != null &&
                EF.Functions.ILike(user.UserName, pattern))
            .OrderBy(user => user.UserName)
            .Take(20)
            .Select(user => new UserDto
            {
                Id = user.Id.ToString(),
                Username = user.UserName!,
                DisplayName = user.DisplayName
            })
            .ToListAsync();
    }

    private static UserDto ToDto(ApplicationUser user)
    {
        return new UserDto
        {
            Id = user.Id.ToString(),
            Username = user.UserName ?? string.Empty,
            DisplayName = user.DisplayName
        };
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }
}
