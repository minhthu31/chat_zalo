using System.Collections.Concurrent;
using Chat.Application.DTOs.Friendships;
using Chat.Application.Interfaces;
using Chat.Application.Models.Groups;
using Chat.Application.Services;
using Xunit;

namespace Chat.Tests.Groups;

public class GroupPermissionServiceTests
{
    private static readonly Guid G = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid Member = Guid.NewGuid();
    private static readonly Guid Member2 = Guid.NewGuid();
    private static readonly Guid Outsider = Guid.NewGuid();
    private static readonly Guid Left = Guid.NewGuid();
    private static readonly Guid Removed = Guid.NewGuid();

    private readonly FakeMembershipQuery _db = new();
    private readonly FakeFriendships _friends = new();
    private readonly GroupPermissionService _sut;

    public GroupPermissionServiceTests()
    {
        _db.Set(G, Admin, GroupRole.Admin, GroupMemberStatus.Active);
        _db.Set(G, Member, GroupRole.Member, GroupMemberStatus.Active);
        _db.Set(G, Member2, GroupRole.Member, GroupMemberStatus.Active);
        _db.Set(G, Left, GroupRole.Member, GroupMemberStatus.Left);
        _db.Set(G, Removed, GroupRole.Member, GroupMemberStatus.Removed);
        _sut = new GroupPermissionService(_db, _friends);
    }

    // ---------- Thành viên & vai trò ----------

    [Fact]
    public async Task Role_IsReportedCorrectly()
    {
        Assert.Equal(GroupRole.Admin, await _sut.GetRoleAsync(G, Admin));
        Assert.Equal(GroupRole.Member, await _sut.GetRoleAsync(G, Member));
        Assert.True(await _sut.IsAdminAsync(G, Admin));
        Assert.False(await _sut.IsAdminAsync(G, Member));
    }

    [Fact]
    public async Task NonMember_IsDenied()
    {
        var r = await _sut.GetAccessAsync(G, Outsider);
        Assert.False(r.Allowed);
        Assert.Equal(GroupPermissionErrors.NotAMember, r.ErrorCode);
    }

    [Fact]
    public async Task UnknownGroup_IsDenied()
        => Assert.False((await _sut.GetAccessAsync(Guid.NewGuid(), Admin)).Allowed);

    [Fact]
    public async Task LeftAndRemoved_ReturnSpecificErrors()
    {
        Assert.Equal(GroupPermissionErrors.LeftGroup, (await _sut.GetAccessAsync(G, Left)).ErrorCode);
        Assert.Equal(GroupPermissionErrors.RemovedFromGroup, (await _sut.GetAccessAsync(G, Removed)).ErrorCode);
    }

    [Fact]
    public async Task DisbandedGroup_DeniesEveryone()
    {
        _db.Disband(G);
        var r = await _sut.GetAccessAsync(G, Admin);
        Assert.False(r.Allowed);
        Assert.Equal(GroupPermissionErrors.GroupDisbanded, r.ErrorCode);
    }

    // ---------- Admin-only ----------

    [Fact]
    public async Task Admin_CanRemoveOtherMember()
        => Assert.True((await _sut.CanRemoveMemberAsync(G, Admin, Member)).Allowed);

    [Fact]
    public async Task Member_CannotRemoveAnyone()
    {
        var r = await _sut.CanRemoveMemberAsync(G, Member, Member2);
        Assert.False(r.Allowed);
        Assert.Equal(GroupPermissionErrors.AdminRequired, r.ErrorCode);
    }

    [Fact]
    public async Task Admin_CannotRemoveSelf_OrNonMember()
    {
        Assert.Equal(GroupPermissionErrors.InvalidTarget, (await _sut.CanRemoveMemberAsync(G, Admin, Admin)).ErrorCode);
        Assert.Equal(GroupPermissionErrors.TargetNotMember, (await _sut.CanRemoveMemberAsync(G, Admin, Outsider)).ErrorCode);
        Assert.Equal(GroupPermissionErrors.TargetNotMember, (await _sut.CanRemoveMemberAsync(G, Admin, Left)).ErrorCode);
    }

    [Fact]
    public async Task OnlyAdmin_CanDisband()
    {
        Assert.True((await _sut.CanDisbandGroupAsync(G, Admin)).Allowed);
        Assert.Equal(GroupPermissionErrors.AdminRequired, (await _sut.CanDisbandGroupAsync(G, Member)).ErrorCode);
        Assert.Equal(GroupPermissionErrors.NotAMember, (await _sut.CanDisbandGroupAsync(G, Outsider)).ErrorCode);
    }

    [Fact]
    public async Task OnlyAdmin_CanProposeAdminTransfer_ToActiveMember()
    {
        Assert.True((await _sut.CanProposeAdminTransferAsync(G, Admin, Member)).Allowed);
        Assert.Equal(GroupPermissionErrors.AdminRequired, (await _sut.CanProposeAdminTransferAsync(G, Member, Member2)).ErrorCode);
        Assert.Equal(GroupPermissionErrors.InvalidTarget, (await _sut.CanProposeAdminTransferAsync(G, Admin, Admin)).ErrorCode);
        Assert.Equal(GroupPermissionErrors.TargetNotMember, (await _sut.CanProposeAdminTransferAsync(G, Admin, Left)).ErrorCode);
        Assert.Equal(GroupPermissionErrors.TargetNotMember, (await _sut.CanProposeAdminTransferAsync(G, Admin, Outsider)).ErrorCode);
    }

    // ---------- Thêm thành viên theo Friendships ----------

    [Fact]
    public async Task Member_CanAddOwnFriend()
    {
        _friends.Add(Member, Outsider);
        Assert.True((await _sut.CanAddMemberAsync(G, Member, Outsider)).Allowed);
    }

    [Fact]
    public async Task Member_CannotAddSomeoneElsesFriend()
    {
        _friends.Add(Admin, Outsider); // bạn của Admin, KHÔNG phải của Member
        var r = await _sut.CanAddMemberAsync(G, Member, Outsider);
        Assert.False(r.Allowed);
        Assert.Equal(GroupPermissionErrors.NotFriends, r.ErrorCode);
    }

    [Fact]
    public async Task AddMember_RejectsAlreadyMember_Self_AndNonMemberActor()
    {
        _friends.Add(Member, Member2);
        Assert.Equal(GroupPermissionErrors.AlreadyMember, (await _sut.CanAddMemberAsync(G, Member, Member2)).ErrorCode);
        Assert.Equal(GroupPermissionErrors.InvalidTarget, (await _sut.CanAddMemberAsync(G, Member, Member)).ErrorCode);
        _friends.Add(Outsider, Guid.NewGuid());
        Assert.Equal(GroupPermissionErrors.NotAMember, (await _sut.CanAddMemberAsync(G, Outsider, Guid.NewGuid())).ErrorCode);
    }

    [Fact]
    public async Task PreviouslyLeftFriend_CanBeAddedAgain()
    {
        _friends.Add(Member, Left);
        Assert.True((await _sut.CanAddMemberAsync(G, Member, Left)).Allowed);
    }

    // ---------- Tin nhắn & SignalR ----------

    [Fact]
    public async Task ActiveMembers_CanSendViewJoinReceive()
    {
        foreach (var u in new[] { Admin, Member })
        {
            Assert.True((await _sut.CanSendMessageAsync(G, u)).Allowed);
            Assert.True((await _sut.CanViewMessagesAsync(G, u)).Allowed);
            Assert.True((await _sut.CanJoinRealtimeAsync(G, u)).Allowed);
            Assert.True((await _sut.CanReceiveRealtimeAsync(G, u)).Allowed);
        }
    }

    [Theory]
    [InlineData("outsider")]
    [InlineData("left")]
    [InlineData("removed")]
    public async Task NonActive_CannotSendViewJoinReceive(string who)
    {
        var u = who switch { "left" => Left, "removed" => Removed, _ => Outsider };
        Assert.False((await _sut.CanSendMessageAsync(G, u)).Allowed);
        Assert.False((await _sut.CanViewMessagesAsync(G, u)).Allowed);
        Assert.False((await _sut.CanJoinRealtimeAsync(G, u)).Allowed);
        Assert.False((await _sut.CanReceiveRealtimeAsync(G, u)).Allowed);
    }

    [Fact]
    public async Task RealtimeGroupIds_ContainsOnlyActiveMemberships()
    {
        var other = Guid.NewGuid();
        _db.Set(other, Member, GroupRole.Member, GroupMemberStatus.Removed);
        var ids = await _sut.GetRealtimeGroupIdsAsync(Member);
        Assert.Contains(G, ids);
        Assert.DoesNotContain(other, ids);
    }

    [Fact]
    public async Task ThrowIfDenied_ThrowsWithErrorCode()
    {
        var r = await _sut.CanSendMessageAsync(G, Outsider);
        var ex = Assert.Throws<GroupPermissionException>(() => r.ThrowIfDenied());
        Assert.Equal(GroupPermissionErrors.NotAMember, ex.ErrorCode);
    }

    // ---------- Thay đổi quyền đồng thời ----------

    [Fact]
    public async Task RemovedMember_IsDeniedImmediately_NoStaleCache()
    {
        Assert.True((await _sut.CanSendMessageAsync(G, Member)).Allowed);
        _db.Set(G, Member, GroupRole.Member, GroupMemberStatus.Removed);
        Assert.False((await _sut.CanSendMessageAsync(G, Member)).Allowed);
    }

    [Fact]
    public async Task DemotedAdmin_LosesAdminRightsImmediately()
    {
        Assert.True((await _sut.CanDisbandGroupAsync(G, Admin)).Allowed);
        _db.Set(G, Admin, GroupRole.Member, GroupMemberStatus.Active);
        Assert.False((await _sut.CanDisbandGroupAsync(G, Admin)).Allowed);
    }

    [Fact]
    public async Task ParallelChecks_WhileMembershipChanges_AreConsistentAfterChange()
    {
        // 200 yêu cầu song song trong lúc quyền của Member bị thu hồi.
        var checks = Enumerable.Range(0, 200).Select(async i =>
        {
            if (i == 100) _db.Set(G, Member, GroupRole.Member, GroupMemberStatus.Removed);
            return await _sut.CanSendMessageAsync(G, Member);
        }).ToArray();

        var results = await Task.WhenAll(checks);

        // Không crash / không trạng thái nửa vời: mỗi kết quả hoặc Allowed hoặc có ErrorCode rõ ràng.
        Assert.All(results, r => Assert.True(r.Allowed || r.ErrorCode == GroupPermissionErrors.RemovedFromGroup));

        // Sau khi thay đổi hoàn tất, mọi yêu cầu mới đều bị từ chối.
        var after = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => _sut.CanSendMessageAsync(G, Member)));
        Assert.All(after, r => Assert.False(r.Allowed));
    }

    [Fact]
    public async Task ConcurrentTransferAndRemove_AdminRevokedThenDenied()
    {
        // Admin cũ bị hạ quyền song song với nhiều thao tác quản trị: sau đó mọi thao tác Admin phải bị từ chối.
        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
        {
            if (i == 25) _db.Set(G, Admin, GroupRole.Member, GroupMemberStatus.Active);
            return await _sut.CanRemoveMemberAsync(G, Admin, Member);
        }));
        await Task.WhenAll(tasks);

        Assert.False((await _sut.CanRemoveMemberAsync(G, Admin, Member)).Allowed);
        Assert.False((await _sut.CanDisbandGroupAsync(G, Admin)).Allowed);
    }

        // ---------- Ánh xạ mã lỗi theo API_CONTRACT ----------

    [Fact]
    public async Task ErrorCodes_AndHttpStatus_MatchContract()
    {
        var outsider = await _sut.CanSendMessageAsync(G, Outsider);
        Assert.Equal("GROUP_ACCESS_DENIED", outsider.ErrorCode);
        Assert.Equal(403, outsider.HttpStatus);

        var member = await _sut.CanDisbandGroupAsync(G, Member);
        Assert.Equal("GROUP_ACCESS_DENIED", member.ErrorCode);
        Assert.Equal(403, member.HttpStatus);

        var notFriend = await _sut.CanAddMemberAsync(G, Member, Outsider);
        Assert.Equal("NOT_FRIEND", notFriend.ErrorCode);
        Assert.Equal(403, notFriend.HttpStatus);

        _friends.Add(Member, Member2);
        var dup = await _sut.CanAddMemberAsync(G, Member, Member2);
        Assert.Equal("ALREADY_GROUP_MEMBER", dup.ErrorCode);
        Assert.Equal(409, dup.HttpStatus);

        var missing = await _sut.CanRemoveMemberAsync(G, Admin, Outsider);
        Assert.Equal("NOT_FOUND", missing.ErrorCode);
        Assert.Equal(404, missing.HttpStatus);

        var self = await _sut.CanRemoveMemberAsync(G, Admin, Admin);
        Assert.Equal("VALIDATION_ERROR", self.ErrorCode);
        Assert.Equal(400, self.HttpStatus);
    }// ---------- Fakes (thread-safe) ----------

    private sealed class FakeMembershipQuery : IGroupMembershipQuery
    {
        private readonly ConcurrentDictionary<(Guid, Guid), (GroupRole Role, GroupMemberStatus Status)> _rows = new();
        private readonly ConcurrentDictionary<Guid, bool> _disbanded = new();

        public void Set(Guid g, Guid u, GroupRole role, GroupMemberStatus status) => _rows[(g, u)] = (role, status);
        public void Disband(Guid g) => _disbanded[g] = true;

        public Task<GroupMembershipSnapshot?> GetMembershipAsync(Guid groupId, Guid userId, CancellationToken ct = default)
        {
            GroupMembershipSnapshot? s = _rows.TryGetValue((groupId, userId), out var r)
                ? new GroupMembershipSnapshot(groupId, userId, r.Role, r.Status, !_disbanded.ContainsKey(groupId))
                : null;
            return Task.FromResult(s);
        }

        public Task<IReadOnlyList<Guid>> GetActiveGroupIdsAsync(Guid userId, CancellationToken ct = default)
        {
            IReadOnlyList<Guid> ids = _rows
                .Where(kv => kv.Key.Item2 == userId
                             && kv.Value.Status == GroupMemberStatus.Active
                             && !_disbanded.ContainsKey(kv.Key.Item1))
                .Select(kv => kv.Key.Item1)
                .ToList();
            return Task.FromResult(ids);
        }
    }

    private sealed class FakeFriendships : IFriendshipService
    {
        private readonly ConcurrentDictionary<(Guid, Guid), bool> _pairs = new();

        public void Add(Guid a, Guid b) { _pairs[(a, b)] = true; _pairs[(b, a)] = true; }

        public Task<bool> AreFriendsAsync(Guid userAId, Guid userBId)
            => Task.FromResult(_pairs.ContainsKey((userAId, userBId)));

        public Task<FriendshipDto> SendRequestAsync(Guid currentUserId, Guid targetUserId) => throw new NotImplementedException();
        public Task AcceptAsync(Guid currentUserId, Guid requesterUserId) => throw new NotImplementedException();
        public Task RejectAsync(Guid currentUserId, Guid requesterUserId) => throw new NotImplementedException();
        public Task RemoveAsync(Guid currentUserId, Guid targetUserId) => throw new NotImplementedException();
        public Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId) => throw new NotImplementedException();
        public Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId) => throw new NotImplementedException();
    }
}
