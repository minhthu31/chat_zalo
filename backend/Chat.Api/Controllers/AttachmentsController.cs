using System.Security.Claims;
using Chat.Api.Filters;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Chat.Api.Controllers;

[ApiController]
[Authorize]
public sealed class AttachmentsController(IAttachmentService attachments, ILogger<AttachmentsController> logger) : ControllerBase
{
    private const long RequestLimit = 11 * 1024 * 1024; // room for multipart headers; service enforces 10 MiB/file

    [HttpPost("api/conversations/{id:guid}/attachments")]
    [ValidateAntiforgeryJson]
    [AttachmentUploadLimit]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    public async Task<IActionResult> Upload(Guid id, CancellationToken ct)
    {
        if (!TryUser(out var user)) return Unauthorized();
        try
        {
            if (Request.ContentLength > RequestLimit)
                return StatusCode(413, new { code = "FILE_TOO_LARGE", message = "File must not exceed 10 MiB." });
            if (!Request.HasFormContentType || Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true)
                return StatusCode(415, new { code = "UNSUPPORTED_FILE_TYPE", message = "Use multipart/form-data with field file." });
            var form = await Request.ReadFormAsync(ct);
            if (form.Files.Count != 1 || form.Files[0].Name != "file")
                return BadRequest(new { code = "VALIDATION_ERROR", message = "Exactly one file field is required." });
            var file = form.Files[0];
            await using var stream = file.OpenReadStream();
            var dto = await attachments.UploadAsync(user, id, file.FileName, file.ContentType, file.Length, stream, ct);
            return Created(dto.ContentUrl, dto);
        }
        catch (AttachmentServiceException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message }); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        { return StatusCode(413, new { code = "FILE_TOO_LARGE", message = "File must not exceed 10 MiB." }); }
        catch (InvalidDataException ex) when (ex.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase))
        { return StatusCode(413, new { code = "FILE_TOO_LARGE", message = "File must not exceed 10 MiB." }); }
        catch (InvalidDataException)
        { return BadRequest(new { code = "VALIDATION_ERROR", message = "Invalid multipart request." }); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return Unexpected(ex); }
    }

    [HttpGet("api/attachments/{attachmentId:guid}/content")]
    public Task<IActionResult> Content(Guid attachmentId, CancellationToken ct) => Read(attachmentId, false, ct);

    [HttpGet("api/attachments/{attachmentId:guid}/download")]
    public Task<IActionResult> Download(Guid attachmentId, CancellationToken ct) => Read(attachmentId, true, ct);

    private async Task<IActionResult> Read(Guid id, bool download, CancellationToken ct)
    {
        if (!TryUser(out var user)) return Unauthorized();
        try
        {
            var file = await attachments.OpenContentAsync(user, id, ct);
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return download ? File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true)
                : File(file.Content, file.ContentType, enableRangeProcessing: true);
        }
        catch (AttachmentServiceException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message }); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return Unexpected(ex); }
    }

    private bool TryUser(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id) && id != Guid.Empty;
    private ObjectResult Unexpected(Exception ex)
    {
        logger.LogError(ex, "Attachment request failed");
        return StatusCode(500, new { code = "INTERNAL_ERROR", message = "Unable to process the attachment." });
    }
}

// Reject oversized requests before antiforgery attempts to read multipart form data.
internal sealed class AttachmentUploadLimitAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
{
    public int Order => int.MinValue;
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.Request.ContentLength > 11 * 1024 * 1024)
            context.Result = new ObjectResult(new { code = "FILE_TOO_LARGE", message = "File must not exceed 10 MiB." })
            { StatusCode = StatusCodes.Status413PayloadTooLarge };
    }
}
