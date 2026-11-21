using System.Security.Claims;
using System.Text.Json;
using Chat.Api.Filters;
using Chat.Application.DTOs.Profiles;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Chat.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class ProfilesController(
    IProfileService profileService,
    IAvatarService avatarService,
    ILogger<ProfilesController> logger) : ControllerBase
{
    private const long RequestLimit = 3 * 1024 * 1024; // 3 MiB multipart limit; service enforces 2 MiB avatar file

    [HttpGet("{userId:guid}/profile")]
    public async Task<IActionResult> GetProfile(Guid userId, CancellationToken ct)
    {
        if (!TryUser(out var currentUserId)) return Unauthorized();
        try
        {
            var profile = await profileService.GetAsync(currentUserId, userId, ct);
            return Ok(profile);
        }
        catch (ProfileServiceException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return Unexpected(ex);
        }
    }

    [HttpPut("me/profile")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> UpdateProfile([FromBody] JsonElement body, CancellationToken ct)
    {
        if (!TryUser(out var currentUserId)) return Unauthorized();
        try
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return BadRequest(new { code = "VALIDATION_ERROR", message = "Invalid JSON object." });
            }

            JsonProperty? displayNameProp = null;
            JsonProperty? bioProp = null;

            foreach (var prop in body.EnumerateObject())
            {
                if (string.Equals(prop.Name, "displayName", StringComparison.OrdinalIgnoreCase))
                {
                    displayNameProp = prop;
                }
                else if (string.Equals(prop.Name, "bio", StringComparison.OrdinalIgnoreCase))
                {
                    bioProp = prop;
                }
            }

            if (!displayNameProp.HasValue || displayNameProp.Value.Value.ValueKind != JsonValueKind.String)
            {
                return BadRequest(new { code = "INVALID_DISPLAY_NAME", message = "DisplayName is required and must be a string." });
            }

            if (!bioProp.HasValue || bioProp.Value.Value.ValueKind != JsonValueKind.String)
            {
                return BadRequest(new { code = "INVALID_BIO", message = "Bio is required and must be a string." });
            }

            var request = new UpdatePublicProfileRequest
            {
                DisplayName = displayNameProp.Value.Value.GetString()!,
                Bio = bioProp.Value.Value.GetString()!
            };

            var profile = await profileService.UpdateAsync(currentUserId, request, ct);
            return Ok(profile);
        }
        catch (ProfileServiceException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return Unexpected(ex);
        }
    }

    [HttpPut("me/avatar")]
    [ValidateAntiforgeryJson]
    [AvatarUploadLimit]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    public async Task<IActionResult> UploadAvatar(CancellationToken ct)
    {
        if (!TryUser(out var currentUserId)) return Unauthorized();
        try
        {
            if (Request.ContentLength > RequestLimit)
            {
                return StatusCode(413, new { code = "FILE_TOO_LARGE", message = "Avatar file must not exceed 2 MiB." });
            }

            if (!Request.HasFormContentType || Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true)
            {
                return StatusCode(415, new { code = "UNSUPPORTED_FILE_TYPE", message = "Use multipart/form-data with field file." });
            }

            var form = await Request.ReadFormAsync(ct);
            if (form.Files.Count != 1 || form.Files[0].Name != "file")
            {
                return BadRequest(new { code = "VALIDATION_ERROR", message = "Exactly one file field is required." });
            }

            var file = form.Files[0];
            await using var stream = file.OpenReadStream();
            var profile = await avatarService.SetAsync(currentUserId, file.FileName, file.ContentType, file.Length, stream, ct);
            return Ok(profile);
        }
        catch (ProfileServiceException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        {
            return StatusCode(413, new { code = "FILE_TOO_LARGE", message = "Avatar file must not exceed 2 MiB." });
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(413, new { code = "FILE_TOO_LARGE", message = "Avatar file must not exceed 2 MiB." });
        }
        catch (InvalidDataException)
        {
            return BadRequest(new { code = "VALIDATION_ERROR", message = "Invalid multipart request." });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return Unexpected(ex);
        }
    }

    [HttpDelete("me/avatar")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> RemoveAvatar(CancellationToken ct)
    {
        if (!TryUser(out var currentUserId)) return Unauthorized();
        try
        {
            await avatarService.RemoveAsync(currentUserId, ct);
            return NoContent();
        }
        catch (ProfileServiceException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return Unexpected(ex);
        }
    }

    [HttpGet("{userId:guid}/avatar")]
    public async Task<IActionResult> GetAvatar(Guid userId, CancellationToken ct)
    {
        if (!TryUser(out var currentUserId)) return Unauthorized();
        try
        {
            var file = await avatarService.OpenContentAsync(currentUserId, userId, ct);
            if (file is null)
            {
                return NotFound(new { code = "ATTACHMENT_NOT_FOUND", message = "Avatar not found." });
            }

            Response.Headers.CacheControl = "private, no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(file.Content, file.ContentType, enableRangeProcessing: true);
        }
        catch (ProfileServiceException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return Unexpected(ex);
        }
    }

    private bool TryUser(out Guid id) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id) && id != Guid.Empty;

    private ObjectResult Unexpected(Exception ex)
    {
        logger.LogError(ex, "Profile request failed");
        return StatusCode(StatusCodes.Status500InternalServerError,
            new { code = "INTERNAL_ERROR", message = "Unable to process the profile request." });
    }
}

internal sealed class AvatarUploadLimitAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
{
    public int Order => int.MinValue;
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.Request.ContentLength > 3 * 1024 * 1024)
        {
            context.Result = new ObjectResult(new { code = "FILE_TOO_LARGE", message = "Avatar file must not exceed 2 MiB." })
            { StatusCode = StatusCodes.Status413PayloadTooLarge };
        }
    }
}
