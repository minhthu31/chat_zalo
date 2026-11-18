using Chat.Client.Models;

namespace Chat.Client.Services;

// Quản lý nhóm (hợp đồng Sprint 2, mục 9). Mọi endpoint nằm dưới /api/conversations.
public interface IGroupApiClient
{
    Task<List<GroupDto>> ListAsync();                                             // GET /api/conversations rồi lọc nhóm và lấy tên từng nhóm
    Task<GroupDto> CreateAsync(CreateGroupRequest request);                       // POST   /api/conversations/groups            -> 201
    Task<GroupDto> GetAsync(Guid groupId);                                        // GET    /api/conversations/{id}              -> 200
    Task<List<GroupMemberDto>> GetMembersAsync(Guid groupId);                     // GET    /api/conversations/{id}/members      -> 200
    Task<GroupMemberDto> AddMemberAsync(Guid groupId, Guid userId);               // POST   /api/conversations/{id}/members      -> 201
    Task RemoveMemberAsync(Guid groupId, Guid userId);                            // DELETE /api/conversations/{id}/members/{u}  -> 204 (Admin)
    Task LeaveAsync(Guid groupId);                                                // POST   /api/conversations/{id}/leave        -> 204
    Task<AdminTransferDto> ProposeAdminTransferAsync(Guid groupId, Guid targetUserId); // POST .../admin-transfer                -> 202
    Task<List<AdminTransferDto>> GetPendingAdminTransfersAsync(Guid groupId);
    Task AcceptAdminTransferAsync(Guid groupId, Guid requestId);                  // POST   .../admin-transfer/{r}/accept        -> 204
    Task RejectAdminTransferAsync(Guid groupId, Guid requestId);                  // POST   .../admin-transfer/{r}/reject        -> 204
    Task DisbandAsync(Guid groupId);                                              // DELETE /api/conversations/{id}              -> 204 (Admin)
}
