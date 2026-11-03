using System.Security.Claims;
using Chat.Api.Filters;
using Chat.Application.DTOs.Users;
using Chat.Application.Interfaces;
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

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new
            {
                code = "UNAUTHORIZED",
                message = "User is not authenticated."
            });
        }

        try
        {
            var user = await _userService.GetProfileAsync(userId);
            return Ok(user);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                code = "USER_NOT_FOUND",
                message = "User not found."
            });
        }
    }

    [HttpPatch("me")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest? request)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new
            {
                code = "UNAUTHORIZED",
                message = "User is not authenticated."
            });
        }

        try
        {
            var user = await _userService.UpdateProfileAsync(
                userId, request!);

            return Ok(user);
        }
        catch (ArgumentException ex) when (ex.GetType() == typeof(ArgumentException))
        {
            var message = request is null || string.IsNullOrWhiteSpace(request.DisplayName)
                ? "DisplayName is required."
                : "DisplayName must not exceed 100 characters.";

            return BadRequest(new
            {
                code = "INVALID_DISPLAY_NAME",
                message
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                code = "USER_NOT_FOUND",
                message = "User not found."
            });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    code = "PROFILE_UPDATE_FAILED",
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
                code = "USERNAME_REQUIRED",
                message = "Username is required."
            });
        }

        try
        {
            var users = await _userService.SearchAsync(username);
            return Ok(users);
        }
        catch (ArgumentException ex) when (ex.GetType() == typeof(ArgumentException))
        {
            return BadRequest(new
            {
                code = "INVALID_USERNAME",
                message = "Username must not exceed 100 characters."
            });
        }
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var value = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out userId);
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
        string userId, string displayName)
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
        var pattern = "%" + EscapeLikePattern(username) + "%";

        return await _users.Users
            .AsNoTracking()
            .Where(user =>
                user.UserName != null &&
                EF.Functions.ILike(
                    user.UserName, pattern, "\\"))
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
