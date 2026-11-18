using Chat.Client.Models;

namespace Chat.Client.Services;

// Bản thật của IGroupApiClient: gọi các endpoint nhóm của backend
public class GroupApiClient : IGroupApiClient
{
    private readonly ApiRequestSender _api;

    public GroupApiClient(ApiRequestSender api)
    {
        _api = api;
    }

    // Danh sách hội thoại của backend trả cả chat 1-1 lẫn nhóm và KHÔNG có tên nhóm,
    // nên lọc ra các nhóm rồi hỏi thêm từng nhóm để lấy tên.
    public async Task<List<GroupDto>> ListAsync()
    {
        var conversations = await _api.SendAsync<List<ConversationDto>>(HttpMethod.Get, "api/conversations");
        var groups = new List<GroupDto>();

        foreach (var item in conversations.Where(c => c.Kind == "Group"))
        {
            try
            {
                groups.Add(await GetAsync(item.Id));
            }
            catch (ApiException ex) when (ex.StatusCode is 403 or 404)
            {
                // Nhóm đã giải tán hoặc không còn quyền: bỏ qua, không làm hỏng cả danh sách
            }
        }

        return groups;
    }

    public Task<GroupDto> CreateAsync(CreateGroupRequest request) =>
        _api.SendAsync<GroupDto>(HttpMethod.Post, "api/conversations/groups", request);

    public Task<GroupDto> GetAsync(Guid groupId) =>
        _api.SendAsync<GroupDto>(HttpMethod.Get, $"api/conversations/{groupId}");

    public Task<List<GroupMemberDto>> GetMembersAsync(Guid groupId) =>
        _api.SendAsync<List<GroupMemberDto>>(HttpMethod.Get, $"api/conversations/{groupId}/members");

    public Task<GroupMemberDto> AddMemberAsync(Guid groupId, Guid userId) =>
        _api.SendAsync<GroupMemberDto>(HttpMethod.Post, $"api/conversations/{groupId}/members",
            new AddGroupMemberRequest { UserId = userId });

    public Task RemoveMemberAsync(Guid groupId, Guid userId) =>
        _api.SendAsync(HttpMethod.Delete, $"api/conversations/{groupId}/members/{userId}");

    public Task LeaveAsync(Guid groupId) =>
        _api.SendAsync(HttpMethod.Post, $"api/conversations/{groupId}/leave");

    // 202 Accepted: đề nghị được ghi nhận, chờ người kia đồng ý
    public Task<AdminTransferDto> ProposeAdminTransferAsync(Guid groupId, Guid targetUserId) =>
        _api.SendAsync<AdminTransferDto>(HttpMethod.Post, $"api/conversations/{groupId}/admin-transfer",
            new AdminTransferRequest { TargetUserId = targetUserId });

    public Task AcceptAdminTransferAsync(Guid groupId, Guid requestId) =>
        _api.SendAsync(HttpMethod.Post, $"api/conversations/{groupId}/admin-transfer/{requestId}/accept");

    public Task RejectAdminTransferAsync(Guid groupId, Guid requestId) =>
        _api.SendAsync(HttpMethod.Post, $"api/conversations/{groupId}/admin-transfer/{requestId}/reject");

    public Task DisbandAsync(Guid groupId) =>
        _api.SendAsync(HttpMethod.Delete, $"api/conversations/{groupId}");
}
