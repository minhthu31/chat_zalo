using System.Security.Claims;
using Chat.Api.Filters;
using Chat.Application.DTOs.Auth;
using Chat.Application.DTOs.Groups;
using Chat.Application.Interfaces;
using Chat.Application.Models.Groups;
using Chat.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

[Authorize]
[ApiController]
public class GroupsController : ControllerBase
{
    private readonly IGroupService _groupService;

    public GroupsController(IGroupService groupService)
    {
        _groupService = groupService;
    }

    private Guid? ResolveCurrentUserId()
    {
        if (User.Identity?.IsAuthenticated != true)
            return null;

        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private IActionResult HandleException(Exception ex)
    {
        return ex switch
        {
            GroupServiceException gex => StatusCode(gex.StatusCode, new { code = gex.Code, message = gex.Message }),
            GroupPermissionException pex => StatusCode(pex.HttpStatus, new { code = pex.ErrorCode, message = pex.Message }),
            KeyNotFoundException kex => NotFound(new { code = "NOT_FOUND", message = kex.Message }),
            ArgumentException aex => BadRequest(new { code = "VALIDATION_ERROR", message = aex.Message }),
            UnauthorizedAccessException uex => StatusCode(StatusCodes.Status403Forbidden, new { code = "GROUP_ACCESS_DENIED", message = uex.Message }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { code = "INTERNAL_ERROR", message = ex.Message })
        };
    }

    // 1. Tạo nhóm (Create Group)
    [HttpPost("api/conversations/groups")]
    [HttpPost("api/groups")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
    {
        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return Unauthorized(new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Unauthorized" });

        if (!ModelState.IsValid)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "Dữ liệu không hợp lệ." });

        try
        {
            var result = await _groupService.CreateGroupAsync(currentUserId.Value, request);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // 2. Xem thông tin nhóm (View Group Info)
    [HttpGet("api/conversations/{id:guid}")]
    [HttpGet("api/groups/{id:guid}")]
    public async Task<IActionResult> GetGroup(Guid id)
    {
        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return Unauthorized(new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Unauthorized" });

        try
        {
            var result = await _groupService.GetGroupAsync(currentUserId.Value, id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // 2b. Xem thành viên nhóm (View Group Members)
    [HttpGet("api/conversations/{id:guid}/members")]
    [HttpGet("api/groups/{id:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid id)
    {
        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return Unauthorized(new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Unauthorized" });

        try
        {
            var result = await _groupService.GetMembersAsync(currentUserId.Value, id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // 3. Thêm thành viên (Add Member)
    [HttpPost("api/conversations/{id:guid}/members")]
    [HttpPost("api/groups/{id:guid}/members")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddGroupMemberRequest request)
    {
        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return Unauthorized(new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Unauthorized" });

        if (!ModelState.IsValid || request.UserId == Guid.Empty)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "UserId không hợp lệ." });

        try
        {
            var result = await _groupService.AddMemberAsync(currentUserId.Value, id, request.UserId);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // 4. Xóa thành viên (Remove Member)
    [HttpDelete("api/conversations/{id:guid}/members/{userId:guid}")]
    [HttpDelete("api/groups/{id:guid}/members/{userId:guid}")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId)
    {
        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return Unauthorized(new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Unauthorized" });

        try
        {
            await _groupService.RemoveMemberAsync(currentUserId.Value, id, userId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // 5. Chuyển quyền Admin (Propose Admin Transfer)
    [HttpPost("api/conversations/{id:guid}/admin-transfer")]
    [HttpPost("api/groups/{id:guid}/admin-transfer")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> ProposeAdminTransfer(Guid id, [FromBody] AdminTransferRequest request)
    {
        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return Unauthorized(new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Unauthorized" });

        if (!ModelState.IsValid || request.TargetUserId == Guid.Empty)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "TargetUserId không hợp lệ." });

        try
        {
            var result = await _groupService.ProposeAdminTransferAsync(currentUserId.Value, id, request.TargetUserId);
            return StatusCode(StatusCodes.Status202Accepted, result);
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // 5b. Xác nhận đồng ý nhận quyền Admin (Accept Admin Transfer)
    [HttpPost("api/conversations/{id:guid}/admin-transfer/{requestId:guid}/accept")]
    [HttpPost("api/groups/{id:guid}/admin-transfer/{requestId:guid}/accept")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> AcceptAdminTransfer(Guid id, Guid requestId)
    {
        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return Unauthorized(new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Unauthorized" });

        try
        {
            await _groupService.AcceptAdminTransferAsync(currentUserId.Value, id, requestId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // 5c. Từ chối nhận quyền Admin (Reject Admin Transfer)
    [HttpPost("api/conversations/{id:guid}/admin-transfer/{requestId:guid}/reject")]
    [HttpPost("api/groups/{id:guid}/admin-transfer/{requestId:guid}/reject")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> RejectAdminTransfer(Guid id, Guid requestId)
    {
        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return Unauthorized(new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Unauthorized" });

        try
        {
            await _groupService.RejectAdminTransferAsync(currentUserId.Value, id, requestId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // 6. Rời nhóm (Leave Group)
    [HttpPost("api/conversations/{id:guid}/leave")]
    [HttpPost("api/groups/{id:guid}/leave")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> LeaveGroup(Guid id)
    {
        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return Unauthorized(new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Unauthorized" });

        try
        {
            await _groupService.LeaveGroupAsync(currentUserId.Value, id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // 7. Giải tán nhóm (Dissolve/Delete Group)
    [HttpDelete("api/conversations/{id:guid}")]
    [HttpDelete("api/groups/{id:guid}")]
    [ValidateAntiforgeryJson]
    public async Task<IActionResult> DisbandGroup(Guid id)
    {
        var currentUserId = ResolveCurrentUserId();
        if (currentUserId is null)
            return Unauthorized(new AuthErrorResponse { Code = "UNAUTHORIZED", Message = "Unauthorized" });

        try
        {
            await _groupService.DisbandGroupAsync(currentUserId.Value, id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }
}
