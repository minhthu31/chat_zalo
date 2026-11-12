using Chat.Application.Interfaces;
using Chat.Application.Models.Groups;
using static Chat.Application.Models.Groups.GroupPermissionErrors;

namespace Chat.Application.Services;

public sealed class GroupPermissionService : IGroupPermissionService
{
    private readonly IGroupMembershipQuery _memberships;
    private readonly IFriendshipService _friendships;

    public GroupPermissionService(IGroupMembershipQuery memberships, IFriendshipService friendships)
    {
        _memberships = memberships;
        _friendships = friendships;
    }

    // ---------- Thành viên / vai trò ----------

    public async Task<PermissionResult> GetAccessAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var m = await _memberships.GetMembershipAsync(groupId, userId, ct);
        return Evaluate(m);
    }

    public async Task<bool> IsActiveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default)
        => (await GetAccessAsync(groupId, userId, ct)).Allowed;

    public async Task<bool> IsAdminAsync(Guid groupId, Guid userId, CancellationToken ct = default)
        => (await GetAccessAsync(groupId, userId, ct)).ActorRole == GroupRole.Admin;

    public async Task<GroupRole?> GetRoleAsync(Guid groupId, Guid userId, CancellationToken ct = default)
        => (await GetAccessAsync(groupId, userId, ct)).ActorRole;

    // ---------- Thao tác quản lý ----------

    public async Task<PermissionResult> CanRemoveMemberAsync(
        Guid groupId, Guid actorId, Guid targetUserId, CancellationToken ct = default)
    {
        var admin = await RequireAdminAsync(groupId, actorId, ct);
        if (!admin.Allowed) return admin;

        if (actorId == targetUserId)
            return PermissionResult.Deny(InvalidTarget, "Không thể tự xóa mình; hãy dùng chức năng rời nhóm.");

        var target = await GetAccessAsync(groupId, targetUserId, ct);
        if (!target.Allowed)
            return PermissionResult.Deny(TargetNotMember, "Người bị xóa không phải thành viên đang hoạt động của nhóm.");

        return admin;
    }

    public Task<PermissionResult> CanDisbandGroupAsync(Guid groupId, Guid actorId, CancellationToken ct = default)
        => RequireAdminAsync(groupId, actorId, ct);

    public async Task<PermissionResult> CanAddMemberAsync(
        Guid groupId, Guid actorId, Guid targetUserId, CancellationToken ct = default)
    {
        var actor = await GetAccessAsync(groupId, actorId, ct);
        if (!actor.Allowed) return actor;

        if (actorId == targetUserId)
            return PermissionResult.Deny(InvalidTarget, "Không thể tự thêm chính mình.");

        if ((await GetAccessAsync(groupId, targetUserId, ct)).Allowed)
            return PermissionResult.Deny(AlreadyMember, "Người này đã là thành viên của nhóm.");

        // Quy tắc Friendships (S2-04): chỉ thêm bạn bè CỦA CHÍNH MÌNH.
        if (!await _friendships.AreFriendsAsync(actorId, targetUserId))
            return PermissionResult.Deny(NotFriends, "Chỉ được thêm bạn bè của chính mình vào nhóm.");

        return actor;
    }

    public async Task<PermissionResult> CanProposeAdminTransferAsync(
        Guid groupId, Guid actorId, Guid targetUserId, CancellationToken ct = default)
    {
        var admin = await RequireAdminAsync(groupId, actorId, ct);
        if (!admin.Allowed) return admin;

        if (actorId == targetUserId)
            return PermissionResult.Deny(InvalidTarget, "Không thể chuyển quyền quản trị cho chính mình.");

        var target = await GetAccessAsync(groupId, targetUserId, ct);
        if (!target.Allowed)
            return PermissionResult.Deny(TargetNotMember, "Người nhận quyền phải là thành viên đang hoạt động của nhóm.");

        if (target.ActorRole == GroupRole.Admin)
            return PermissionResult.Deny(InvalidTarget, "Người này đã là Admin.");

        return admin;
    }

    // ---------- Tin nhắn ----------

    public Task<PermissionResult> CanSendMessageAsync(Guid groupId, Guid userId, CancellationToken ct = default)
        => GetAccessAsync(groupId, userId, ct);

    public Task<PermissionResult> CanViewMessagesAsync(Guid groupId, Guid userId, CancellationToken ct = default)
        => GetAccessAsync(groupId, userId, ct);

    // ---------- SignalR ----------

    public Task<PermissionResult> CanJoinRealtimeAsync(Guid groupId, Guid userId, CancellationToken ct = default)
        => GetAccessAsync(groupId, userId, ct);

    public Task<PermissionResult> CanReceiveRealtimeAsync(Guid groupId, Guid userId, CancellationToken ct = default)
        => GetAccessAsync(groupId, userId, ct);

    public Task<IReadOnlyList<Guid>> GetRealtimeGroupIdsAsync(Guid userId, CancellationToken ct = default)
        => _memberships.GetActiveGroupIdsAsync(userId, ct);

    // ---------- Helpers ----------

    private async Task<PermissionResult> RequireAdminAsync(Guid groupId, Guid actorId, CancellationToken ct)
    {
        var access = await GetAccessAsync(groupId, actorId, ct);
        if (!access.Allowed) return access;

        return access.ActorRole == GroupRole.Admin
            ? access
            : PermissionResult.Deny(AdminRequired, "Chỉ Admin của nhóm mới được thực hiện thao tác này.");
    }

    private static PermissionResult Evaluate(GroupMembershipSnapshot? m)
    {
        if (m is null)
            return PermissionResult.Deny(NotAMember, "Bạn không thuộc nhóm này.");
        if (!m.GroupIsActive)
            return PermissionResult.Deny(GroupDisbanded, "Nhóm đã được giải tán.");

        return m.Status switch
        {
            GroupMemberStatus.Active => PermissionResult.Allow(m.Role),
            GroupMemberStatus.Left => PermissionResult.Deny(LeftGroup, "Bạn đã rời hoặc bị xóa khỏi nhóm."),
            GroupMemberStatus.Removed => PermissionResult.Deny(RemovedFromGroup, "Bạn đã bị xóa khỏi nhóm."),
            _ => PermissionResult.Deny(NotAMember, "Bạn không thuộc nhóm này.")
        };
    }
}
