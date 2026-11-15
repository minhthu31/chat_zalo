using Chat.Application.DTOs.Groups;

namespace Chat.Application.Interfaces;

public interface IGroupService
{
    Task<GroupDto> CreateGroupAsync(Guid currentUserId, CreateGroupRequest request, CancellationToken ct = default);
    Task<GroupDto> GetGroupAsync(Guid currentUserId, Guid groupId, CancellationToken ct = default);
    Task<List<GroupMemberDto>> GetMembersAsync(Guid currentUserId, Guid groupId, CancellationToken ct = default);
    Task<GroupMemberDto> AddMemberAsync(Guid currentUserId, Guid groupId, Guid targetUserId, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid currentUserId, Guid groupId, Guid targetUserId, CancellationToken ct = default);
    Task<AdminTransferDto> ProposeAdminTransferAsync(Guid currentUserId, Guid groupId, Guid targetUserId, CancellationToken ct = default);
    Task AcceptAdminTransferAsync(Guid currentUserId, Guid groupId, Guid requestId, CancellationToken ct = default);
    Task RejectAdminTransferAsync(Guid currentUserId, Guid groupId, Guid requestId, CancellationToken ct = default);
    Task LeaveGroupAsync(Guid currentUserId, Guid groupId, CancellationToken ct = default);
    Task DisbandGroupAsync(Guid currentUserId, Guid groupId, CancellationToken ct = default);
}
