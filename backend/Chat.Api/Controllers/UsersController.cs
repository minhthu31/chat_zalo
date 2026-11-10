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

    [HttpGet("search/paged")]
    public async Task<IActionResult> SearchUsersPaged(
        [FromQuery] string? username,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return BadRequest(new
            {
                code = "USERNAME_REQUIRED",
                message = "Username is required."
            });
        }

        var pageNumber = page ?? 1;
        var size = pageSize ?? PagedUsersResponse.DefaultPageSize;

        if (pageNumber < 1)
        {
            return BadRequest(new
            {
                code = "INVALID_PAGE",
                message = "Page must be greater than or equal to 1."
            });
        }

        if (size < 1 || size > PagedUsersResponse.MaxPageSize)
        {
            return BadRequest(new
            {
                code = "INVALID_PAGE_SIZE",
                message = $"PageSize must be between 1 and {PagedUsersResponse.MaxPageSize}."
            });
        }

        try
        {
            var result = await _userService.SearchPagedAsync(
                username, pageNumber, size);

            return Ok(result);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ex.ParamName == "page"
                ? new
                {
                    code = "INVALID_PAGE",
                    message = "Page must be greater than or equal to 1."
                }
                : new
                {
                    code = "INVALID_PAGE_SIZE",
                    message = $"PageSize must be between 1 and {PagedUsersResponse.MaxPageSize}."
                });
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
                Id = user.Id,
                Username = user.UserName!,
                DisplayName = user.DisplayName
            })
            .ToListAsync();
    }

    public async Task<(IReadOnlyList<UserDto> Items, int TotalCount)> SearchByUsernamePagedAsync(
        string username,
        int page,
        int pageSize)
    {
        var pattern = "%" + EscapeLikePattern(username) + "%";

        var query = _users.Users
            .AsNoTracking()
            .Where(user =>
                user.UserName != null &&
                EF.Functions.ILike(
                    user.UserName, pattern, "\\"));

        // SELECT COUNT(*) runs in PostgreSQL; no rows are loaded into memory.
        var totalCount = await query.CountAsync();

        // No matches or a page beyond the last one: skip the second query.
        // Computing in long also prevents overflow for very large page values.
        var skip = (long)(page - 1) * pageSize;

        if (totalCount == 0 || skip >= totalCount)
        {
            return (Array.Empty<UserDto>(), totalCount);
        }

        // Stable order: UserName first, Id as tie-breaker, so pages never
        // overlap or skip rows while the data set is unchanged.
        var items = await query
            .OrderBy(user => user.UserName)
            .ThenBy(user => user.Id)
            .Skip((int)skip)
            .Take(pageSize)
            .Select(user => new UserDto
            {
                Id = user.Id,
                Username = user.UserName!,
                DisplayName = user.DisplayName
            })
            .ToListAsync();

        return (items, totalCount);
    }

    private static UserDto ToDto(ApplicationUser user)
    {
        return new UserDto
        {
            Id = user.Id,
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
