using Chat.Client.Models;

namespace Chat.Client.Services.Mock;

// Bản giả của IGroupApiClient: lưu nhóm trong bộ nhớ, giả lập đúng các quy tắc chính của hợp đồng.
// "Tôi" luôn là user01 (cùng người với FakeUserApiClient).
public class FakeGroupApiClient : IGroupApiClient
{
    private static readonly Guid Me = Guid.Parse("550e8400-e29b-41d4-a716-446655440001");

    private class FakeGroup
    {
        public GroupDto Info { get; init; } = new();
        public List<GroupMemberDto> Members { get; } = new();
        public AdminTransferDto? PendingTransfer { get; set; }
    }

    private readonly Dictionary<Guid, FakeGroup> _groups = new();

    public async Task<List<GroupDto>> ListAsync()
    {
        await Task.Delay(300);
        return _groups.Values.Select(g => Copy(g.Info)).ToList();
    }

    public async Task<GroupDto> CreateAsync(CreateGroupRequest request)
    {
        await Task.Delay(300);

        var name = request.Name.Trim();
        if (name == "" || name.Length > 100
            || request.MemberIds.Count == 0
            || request.MemberIds.Distinct().Count() != request.MemberIds.Count)
            throw new ApiException(400, "VALIDATION_ERROR", "Invalid group");

        var group = new FakeGroup { Info = new GroupDto { Id = Guid.NewGuid(), Name = name } };
        group.Members.Add(ToMember(Me, GroupRoles.Admin));

        foreach (var id in request.MemberIds.Where(id => id != Me))
            group.Members.Add(ToMember(id, GroupRoles.Member));

        _groups[group.Info.Id] = group;
        return Copy(group.Info);
    }

    public async Task<GroupDto> GetAsync(Guid groupId)
    {
        await Task.Delay(200);
        return Copy(Find(groupId).Info);
    }

    public async Task<List<GroupMemberDto>> GetMembersAsync(Guid groupId)
    {
        await Task.Delay(200);
        return Find(groupId).Members.Select(Copy).ToList();
    }

    public async Task<GroupMemberDto> AddMemberAsync(Guid groupId, Guid userId)
    {
        await Task.Delay(300);
        var group = Find(groupId);

        if (group.Members.Any(m => m.UserId == userId))
            throw new ApiException(409, "ALREADY_GROUP_MEMBER", "Already a member");

        var member = ToMember(userId, GroupRoles.Member);
        group.Members.Add(member);
        return Copy(member);
    }

    public async Task RemoveMemberAsync(Guid groupId, Guid userId)
    {
        await Task.Delay(300);
        var group = Find(groupId);
        RequireAdmin(group);

        var target = group.Members.FirstOrDefault(m => m.UserId == userId)
                     ?? throw new ApiException(404, "NOT_FOUND", "Member not found");

        if (target.IsAdmin)
            throw new ApiException(403, "GROUP_ACCESS_DENIED", "Cannot remove the admin");

        group.Members.Remove(target);
    }

    public async Task LeaveAsync(Guid groupId)
    {
        await Task.Delay(300);
        var group = Find(groupId);

        var me = group.Members.First(m => m.UserId == Me);
        if (me.IsAdmin)
            throw new ApiException(403, "GROUP_ACCESS_DENIED", "Admin must transfer the role before leaving");

        group.Members.Remove(me);
    }

    public async Task<AdminTransferDto> ProposeAdminTransferAsync(Guid groupId, Guid targetUserId)
    {
        await Task.Delay(300);
        var group = Find(groupId);
        RequireAdmin(group);

        if (group.PendingTransfer is not null)
            throw new ApiException(409, "ADMIN_TRANSFER_PENDING", "A transfer is already pending");

        if (group.Members.All(m => m.UserId != targetUserId || m.IsAdmin))
            throw new ApiException(403, "GROUP_ACCESS_DENIED", "Target must be an active member");

        group.PendingTransfer = new AdminTransferDto
        {
            Id = Guid.NewGuid(),
            ConversationId = groupId,
            FromUserId = Me,
            ToUserId = targetUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        return Copy(group.PendingTransfer);
    }

    // Bản giả cho phép "tôi" đóng vai người nhận để thử luồng đồng ý
    public Task<List<AdminTransferDto>> GetPendingAdminTransfersAsync(Guid groupId)
    {
        var pending = Find(groupId).PendingTransfer;
        return Task.FromResult(pending is not null && pending.Status == "Pending"
            ? new List<AdminTransferDto> { Copy(pending) } : new List<AdminTransferDto>());
    }

    public async Task AcceptAdminTransferAsync(Guid groupId, Guid requestId)
    {
        await Task.Delay(300);
        var group = Find(groupId);
        var transfer = RequirePending(group, requestId);

        foreach (var m in group.Members)
            m.Role = m.UserId == transfer.ToUserId ? GroupRoles.Admin : GroupRoles.Member;

        group.PendingTransfer = null;
    }

    public async Task RejectAdminTransferAsync(Guid groupId, Guid requestId)
    {
        await Task.Delay(300);
        var group = Find(groupId);
        RequirePending(group, requestId);
        group.PendingTransfer = null; // từ chối thì không đổi vai trò
    }

    public async Task DisbandAsync(Guid groupId)
    {
        await Task.Delay(300);
        var group = Find(groupId);
        RequireAdmin(group);
        _groups.Remove(groupId);
    }

    private FakeGroup Find(Guid groupId) =>
        _groups.TryGetValue(groupId, out var g) ? g : throw new ApiException(404, "NOT_FOUND", "Group not found");

    private static void RequireAdmin(FakeGroup group)
    {
        if (!group.Members.Any(m => m.UserId == Me && m.IsAdmin))
            throw new ApiException(403, "GROUP_ACCESS_DENIED", "Admin only");
    }

    private static AdminTransferDto RequirePending(FakeGroup group, Guid requestId)
    {
        if (group.PendingTransfer is null || group.PendingTransfer.Id != requestId)
            throw new ApiException(409, "ADMIN_TRANSFER_ALREADY_RESOLVED", "Transfer already resolved");

        return group.PendingTransfer;
    }

    // Lấy tên từ danh bạ giả; "tôi" thì dùng thông tin của user01
    private static GroupMemberDto ToMember(Guid userId, string role)
    {
        var user = FakeUserApiClient.Directory.FirstOrDefault(u => u.Id == userId);
        return new GroupMemberDto
        {
            UserId = userId,
            Username = userId == Me ? "user01" : user?.Username ?? "unknown",
            DisplayName = userId == Me ? "Nguyen Van A" : user?.DisplayName ?? "Unknown",
            Role = role
        };
    }

    private static GroupDto Copy(GroupDto g) =>
        new() { Id = g.Id, Kind = g.Kind, Name = g.Name, LastMessageSeq = g.LastMessageSeq };

    private static GroupMemberDto Copy(GroupMemberDto m) =>
        new() { UserId = m.UserId, Username = m.Username, DisplayName = m.DisplayName, Role = m.Role };

    private static AdminTransferDto Copy(AdminTransferDto t) =>
        new() { Id = t.Id, ConversationId = t.ConversationId, FromUserId = t.FromUserId, ToUserId = t.ToUserId, Status = t.Status, CreatedAt = t.CreatedAt };
}
