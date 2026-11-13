namespace Chat.Application.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Chat.Api.Controllers;
using Chat.Application.DTOs.Friendships;
using Chat.Application.DTOs.Groups;
using Chat.Application.Interfaces;
using Chat.Application.Models.Groups;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Chat.Infrastructure.Repositories;
using Chat.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

public class GroupServiceTests
{
    private static ChatDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new ChatDbContext(options);
    }

    private static ApplicationUser MakeUser(string suffix) => new()
    {
        Id = Guid.NewGuid(),
        UserName = $"user_{suffix}",
        NormalizedUserName = $"USER_{suffix}",
        DisplayName = $"Display {suffix}"
    };

    private class MockFriendshipService : IFriendshipService
    {
        public HashSet<(Guid, Guid)> FriendPairs { get; } = new();

        public void AddFriendship(Guid a, Guid b)
        {
            FriendPairs.Add((a, b));
            FriendPairs.Add((b, a));
        }

        public Task<bool> AreFriendsAsync(Guid userAId, Guid userBId)
        {
            return Task.FromResult(FriendPairs.Contains((userAId, userBId)));
        }

        public Task<FriendshipDto> SendRequestAsync(Guid currentUserId, Guid targetUserId) => throw new NotImplementedException();
        public Task AcceptAsync(Guid currentUserId, Guid requesterUserId) => throw new NotImplementedException();
        public Task RejectAsync(Guid currentUserId, Guid requesterUserId) => throw new NotImplementedException();
        public Task RemoveAsync(Guid currentUserId, Guid targetUserId) => throw new NotImplementedException();
        public Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId) => throw new NotImplementedException();
        public Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId) => throw new NotImplementedException();
    }

    private static (GroupService service, MockFriendshipService friendships, ChatDbContext db) CreateService(string dbName)
    {
        var db = CreateInMemoryDbContext(dbName);
        var friendships = new MockFriendshipService();
        var memberships = new GroupMembershipQuery(db);
        var permissions = new GroupPermissionService(memberships, friendships);
        var service = new GroupService(db, friendships, permissions);
        return (service, friendships, db);
    }

    // =======================================================================
    // 1. Tạo nhóm (Create Group)
    // =======================================================================

    [Fact]
    public async Task CreateGroupAsync_ValidData_CreatesGroupWithAdminAndMembers()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member1 = MakeUser("m1");
        var member2 = MakeUser("m2");

        db.Users.AddRange(admin, member1, member2);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member1.Id);
        friendships.AddFriendship(admin.Id, member2.Id);

        var request = new CreateGroupRequest
        {
            Name = "Nhóm học tập",
            MemberIds = new List<Guid> { member1.Id, member2.Id }
        };

        var groupDto = await service.CreateGroupAsync(admin.Id, request);

        Assert.NotNull(groupDto);
        Assert.NotEqual(Guid.Empty, groupDto.Id);
        Assert.Equal("Nhóm học tập", groupDto.Name);
        Assert.Equal("Group", groupDto.Kind);

        // Verify members in DB
        var members = await db.ConversationMembers.Where(m => m.ConversationId == groupDto.Id).ToListAsync();
        Assert.Equal(3, members.Count);

        var adminMember = members.Single(m => m.UserId == admin.Id);
        Assert.Equal("Admin", adminMember.Role);

        Assert.All(members.Where(m => m.UserId != admin.Id), m => Assert.Equal("Member", m.Role));
    }

    [Fact]
    public async Task CreateGroupAsync_WhenMemberNotFriend_ThrowsGroupServiceException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var stranger = MakeUser("stranger");

        db.Users.AddRange(admin, stranger);
        await db.SaveChangesAsync();

        var request = new CreateGroupRequest
        {
            Name = "Nhóm test",
            MemberIds = new List<Guid> { stranger.Id }
        };

        var ex = await Assert.ThrowsAsync<GroupServiceException>(() => service.CreateGroupAsync(admin.Id, request));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("NOT_FRIEND", ex.Code);
    }

    [Fact]
    public async Task CreateGroupAsync_EmptyName_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, _, _) = CreateService(dbName);

        var request = new CreateGroupRequest
        {
            Name = "   ",
            MemberIds = new List<Guid> { Guid.NewGuid() }
        };

        var ex = await Assert.ThrowsAsync<GroupServiceException>(() => service.CreateGroupAsync(Guid.NewGuid(), request));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ex.Code);
    }

    [Fact]
    public async Task CreateGroupAsync_DuplicateMemberIds_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member1 = MakeUser("m1");
        db.Users.AddRange(admin, member1);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member1.Id);

        var request = new CreateGroupRequest
        {
            Name = "Group with duplicate member",
            MemberIds = new List<Guid> { member1.Id, member1.Id }
        };

        var ex = await Assert.ThrowsAsync<GroupServiceException>(() => service.CreateGroupAsync(admin.Id, request));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ex.Code);
        Assert.Equal("Danh sách thành viên không được chứa ID trùng lặp.", ex.Message);
    }

    [Fact]
    public async Task CreateGroupAsync_SelfInMemberIds_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member1 = MakeUser("m1");
        db.Users.AddRange(admin, member1);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member1.Id);

        var request = new CreateGroupRequest
        {
            Name = "Group with self",
            MemberIds = new List<Guid> { member1.Id, admin.Id }
        };

        var ex = await Assert.ThrowsAsync<GroupServiceException>(() => service.CreateGroupAsync(admin.Id, request));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ex.Code);
    }

    // =======================================================================
    // 2. Xem thông tin và thành viên nhóm (View Group Info & Members)
    // =======================================================================

    [Fact]
    public async Task GetGroupAndMembersAsync_ActiveMember_ReturnsInfoAndMemberList()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member = MakeUser("member");
        db.Users.AddRange(admin, member);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member.Id);

        var groupDto = await service.CreateGroupAsync(admin.Id, new CreateGroupRequest
        {
            Name = "Testing Room",
            MemberIds = new List<Guid> { member.Id }
        });

        // Member gets group info
        var fetchedGroup = await service.GetGroupAsync(member.Id, groupDto.Id);
        Assert.Equal("Testing Room", fetchedGroup.Name);

        // Member gets member list
        var members = await service.GetMembersAsync(member.Id, groupDto.Id);
        Assert.Equal(2, members.Count);
        Assert.Contains(members, m => m.UserId == admin.Id && m.Role == "Admin");
        Assert.Contains(members, m => m.UserId == member.Id && m.Role == "Member");
    }

    [Fact]
    public async Task GetGroupAsync_NonMember_ThrowsAccessDenied()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member = MakeUser("member");
        var outsider = MakeUser("outsider");
        db.Users.AddRange(admin, member, outsider);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member.Id);

        var groupDto = await service.CreateGroupAsync(admin.Id, new CreateGroupRequest
        {
            Name = "Secret Group",
            MemberIds = new List<Guid> { member.Id }
        });

        var ex = await Assert.ThrowsAsync<GroupServiceException>(() => service.GetGroupAsync(outsider.Id, groupDto.Id));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("GROUP_ACCESS_DENIED", ex.Code);
    }

    // =======================================================================
    // 3. Thêm thành viên (Add Member)
    // =======================================================================

    [Fact]
    public async Task AddMemberAsync_ActiveMemberAddsFriend_Success()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member1 = MakeUser("m1");
        var newFriend = MakeUser("m2");
        db.Users.AddRange(admin, member1, newFriend);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member1.Id);
        friendships.AddFriendship(member1.Id, newFriend.Id); // member1 is friend with newFriend

        var groupDto = await service.CreateGroupAsync(admin.Id, new CreateGroupRequest
        {
            Name = "Group",
            MemberIds = new List<Guid> { member1.Id }
        });

        // member1 invites their friend newFriend
        var addedMember = await service.AddMemberAsync(member1.Id, groupDto.Id, newFriend.Id);
        Assert.NotNull(addedMember);
        Assert.Equal(newFriend.Id, addedMember.UserId);
        Assert.Equal("Member", addedMember.Role);

        // Verify count
        var members = await service.GetMembersAsync(admin.Id, groupDto.Id);
        Assert.Equal(3, members.Count);
    }

    [Fact]
    public async Task AddMemberAsync_WhenAlreadyMember_ThrowsAlreadyGroupMember()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member1 = MakeUser("m1");
        db.Users.AddRange(admin, member1);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member1.Id);

        var groupDto = await service.CreateGroupAsync(admin.Id, new CreateGroupRequest
        {
            Name = "Group",
            MemberIds = new List<Guid> { member1.Id }
        });

        var ex = await Assert.ThrowsAsync<GroupPermissionException>(() =>
            service.AddMemberAsync(admin.Id, groupDto.Id, member1.Id));

        Assert.Equal(409, ex.HttpStatus);
        Assert.Equal("ALREADY_GROUP_MEMBER", ex.ErrorCode);
    }

    // =======================================================================
    // 4. Xóa thành viên (Remove Member)
    // =======================================================================

    [Fact]
    public async Task RemoveMemberAsync_AdminRemovesMember_Success()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member1 = MakeUser("m1");
        db.Users.AddRange(admin, member1);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member1.Id);

        var groupDto = await service.CreateGroupAsync(admin.Id, new CreateGroupRequest
        {
            Name = "Group",
            MemberIds = new List<Guid> { member1.Id }
        });

        // Admin removes member1
        await service.RemoveMemberAsync(admin.Id, groupDto.Id, member1.Id);

        // member1 is no longer in active member list
        var members = await service.GetMembersAsync(admin.Id, groupDto.Id);
        Assert.Single(members);
        Assert.Equal(admin.Id, members[0].UserId);
    }

    [Fact]
    public async Task RemoveMemberAsync_RegularMemberTriesToRemove_ThrowsAccessDenied()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member1 = MakeUser("m1");
        var member2 = MakeUser("m2");
        db.Users.AddRange(admin, member1, member2);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member1.Id);
        friendships.AddFriendship(admin.Id, member2.Id);

        var groupDto = await service.CreateGroupAsync(admin.Id, new CreateGroupRequest
        {
            Name = "Group",
            MemberIds = new List<Guid> { member1.Id, member2.Id }
        });

        // Member1 tries to remove Member2
        var ex = await Assert.ThrowsAsync<GroupPermissionException>(() =>
            service.RemoveMemberAsync(member1.Id, groupDto.Id, member2.Id));

        Assert.Equal(403, ex.HttpStatus);
        Assert.Equal("GROUP_ACCESS_DENIED", ex.ErrorCode);
    }

    // =======================================================================
    // 5. Chuyển quyền Admin có xác nhận (Transfer Admin with Confirmation)
    // =======================================================================

    [Fact]
    public async Task AdminTransfer_ProposeAndAccept_SwapsRolesSuccessfully()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var oldAdmin = MakeUser("oldAdmin");
        var newAdmin = MakeUser("newAdmin");
        db.Users.AddRange(oldAdmin, newAdmin);
        await db.SaveChangesAsync();

        friendships.AddFriendship(oldAdmin.Id, newAdmin.Id);

        var groupDto = await service.CreateGroupAsync(oldAdmin.Id, new CreateGroupRequest
        {
            Name = "Leadership Room",
            MemberIds = new List<Guid> { newAdmin.Id }
        });

        // Old admin proposes transfer
        var transferDto = await service.ProposeAdminTransferAsync(oldAdmin.Id, groupDto.Id, newAdmin.Id);
        Assert.NotNull(transferDto);
        Assert.Equal("Pending", transferDto.Status);

        // New admin accepts transfer
        await service.AcceptAdminTransferAsync(newAdmin.Id, groupDto.Id, transferDto.Id);

        // Verify roles
        var members = await service.GetMembersAsync(newAdmin.Id, groupDto.Id);
        var oldAdminMember = members.Single(m => m.UserId == oldAdmin.Id);
        var newAdminMember = members.Single(m => m.UserId == newAdmin.Id);

        Assert.Equal("Member", oldAdminMember.Role);
        Assert.Equal("Admin", newAdminMember.Role);
    }

    [Fact]
    public async Task AdminTransfer_ProposeAndReject_KeepsRolesUnchanged()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var oldAdmin = MakeUser("oldAdmin");
        var nominee = MakeUser("nominee");
        db.Users.AddRange(oldAdmin, nominee);
        await db.SaveChangesAsync();

        friendships.AddFriendship(oldAdmin.Id, nominee.Id);

        var groupDto = await service.CreateGroupAsync(oldAdmin.Id, new CreateGroupRequest
        {
            Name = "Leadership Room",
            MemberIds = new List<Guid> { nominee.Id }
        });

        var transferDto = await service.ProposeAdminTransferAsync(oldAdmin.Id, groupDto.Id, nominee.Id);

        // Nominee rejects transfer
        await service.RejectAdminTransferAsync(nominee.Id, groupDto.Id, transferDto.Id);

        // Roles remain the same
        var members = await service.GetMembersAsync(oldAdmin.Id, groupDto.Id);
        Assert.Equal("Admin", members.Single(m => m.UserId == oldAdmin.Id).Role);
        Assert.Equal("Member", members.Single(m => m.UserId == nominee.Id).Role);
    }

    // =======================================================================
    // 6. Rời nhóm (Leave Group)
    // =======================================================================

    [Fact]
    public async Task LeaveGroupAsync_RegularMemberLeaves_Success()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member1 = MakeUser("m1");
        db.Users.AddRange(admin, member1);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member1.Id);

        var groupDto = await service.CreateGroupAsync(admin.Id, new CreateGroupRequest
        {
            Name = "Group",
            MemberIds = new List<Guid> { member1.Id }
        });

        // member1 leaves
        await service.LeaveGroupAsync(member1.Id, groupDto.Id);

        // member1 can no longer access group
        var ex = await Assert.ThrowsAsync<GroupServiceException>(() => service.GetGroupAsync(member1.Id, groupDto.Id));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task LeaveGroupAsync_AdminLeavesWithOtherActiveMembers_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member1 = MakeUser("m1");
        db.Users.AddRange(admin, member1);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member1.Id);

        var groupDto = await service.CreateGroupAsync(admin.Id, new CreateGroupRequest
        {
            Name = "Group",
            MemberIds = new List<Guid> { member1.Id }
        });

        // Admin tries to leave without transfer
        var ex = await Assert.ThrowsAsync<GroupServiceException>(() => service.LeaveGroupAsync(admin.Id, groupDto.Id));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ex.Code);
    }

    [Fact]
    public async Task LeaveGroupAsync_AdminSoleMember_DissolvesGroup()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member1 = MakeUser("m1");
        db.Users.AddRange(admin, member1);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member1.Id);

        var groupDto = await service.CreateGroupAsync(admin.Id, new CreateGroupRequest
        {
            Name = "Group",
            MemberIds = new List<Guid> { member1.Id }
        });

        // Member1 leaves first
        await service.LeaveGroupAsync(member1.Id, groupDto.Id);

        // Now Admin is sole member, leaving dissolves the group
        await service.LeaveGroupAsync(admin.Id, groupDto.Id);

        var groupConv = await db.GroupConversations.SingleAsync(g => g.ConversationId == groupDto.Id);
        Assert.NotNull(groupConv.DeletedAt);
    }

    // =======================================================================
    // 7. Giải tán nhóm (Dissolve/Delete Group)
    // =======================================================================

    [Fact]
    public async Task DisbandGroupAsync_AdminDisbands_SetsDeletedAtAndCancelsPendingTransfers()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, friendships, db) = CreateService(dbName);

        var admin = MakeUser("admin");
        var member1 = MakeUser("m1");
        db.Users.AddRange(admin, member1);
        await db.SaveChangesAsync();

        friendships.AddFriendship(admin.Id, member1.Id);

        var groupDto = await service.CreateGroupAsync(admin.Id, new CreateGroupRequest
        {
            Name = "Temporary Group",
            MemberIds = new List<Guid> { member1.Id }
        });

        // Propose a transfer to verify it gets cancelled on disband
        await service.ProposeAdminTransferAsync(admin.Id, groupDto.Id, member1.Id);

        // Admin disbands group
        await service.DisbandGroupAsync(admin.Id, groupDto.Id);

        var groupConv = await db.GroupConversations.SingleAsync(g => g.ConversationId == groupDto.Id);
        Assert.NotNull(groupConv.DeletedAt);

        var pendingTransfers = await db.GroupAdminTransferRequests.Where(r => r.ConversationId == groupDto.Id).ToListAsync();
        Assert.All(pendingTransfers, r => Assert.Equal("Cancelled", r.Status));

        // Group is no longer accessible
        var ex = await Assert.ThrowsAsync<GroupServiceException>(() => service.GetGroupAsync(admin.Id, groupDto.Id));
        Assert.Equal(403, ex.StatusCode);
    }

    // =======================================================================
    // 8. Controller: Xử lý Exception và Ghi Log (Lỗi 500)
    // =======================================================================

    [Fact]
    public async Task GroupsController_UnhandledException_LogsExactMessageAndReturns500InternalError()
    {
        var failingService = new FailingGroupService();
        var logger = new TestLogger<GroupsController>();
        var controller = new GroupsController(failingService, logger);

        var userId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var result = await controller.GetGroup(Guid.NewGuid());
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);

        var value = objectResult.Value;
        Assert.NotNull(value);
        var codeProp = value.GetType().GetProperty("code")?.GetValue(value)?.ToString();
        var msgProp = value.GetType().GetProperty("message")?.GetValue(value)?.ToString();

        Assert.Equal("INTERNAL_ERROR", codeProp);
        Assert.Equal("Đã xảy ra lỗi hệ thống, vui lòng thử lại sau.", msgProp);

        Assert.Single(logger.Logs);
        Assert.Equal(LogLevel.Error, logger.Logs[0].Level);
        Assert.Equal("Lỗi xử lý API quản lý nhóm", logger.Logs[0].Message);
        Assert.NotNull(logger.Logs[0].Exception);
    }

    private class FailingGroupService : IGroupService
    {
        public Task<GroupDto> CreateGroupAsync(Guid currentUserId, CreateGroupRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("Sensitive internal error message");
        public Task<GroupDto> GetGroupAsync(Guid currentUserId, Guid groupId, CancellationToken ct = default)
            => throw new InvalidOperationException("Sensitive internal error message");
        public Task<List<GroupMemberDto>> GetMembersAsync(Guid currentUserId, Guid groupId, CancellationToken ct = default)
            => throw new InvalidOperationException("Sensitive internal error message");
        public Task<GroupMemberDto> AddMemberAsync(Guid currentUserId, Guid groupId, Guid targetUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("Sensitive internal error message");
        public Task RemoveMemberAsync(Guid currentUserId, Guid groupId, Guid targetUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("Sensitive internal error message");
        public Task<AdminTransferDto> ProposeAdminTransferAsync(Guid currentUserId, Guid groupId, Guid targetUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("Sensitive internal error message");
        public Task AcceptAdminTransferAsync(Guid currentUserId, Guid groupId, Guid requestId, CancellationToken ct = default)
            => throw new InvalidOperationException("Sensitive internal error message");
        public Task RejectAdminTransferAsync(Guid currentUserId, Guid groupId, Guid requestId, CancellationToken ct = default)
            => throw new InvalidOperationException("Sensitive internal error message");
        public Task LeaveGroupAsync(Guid currentUserId, Guid groupId, CancellationToken ct = default)
            => throw new InvalidOperationException("Sensitive internal error message");
        public Task DisbandGroupAsync(Guid currentUserId, Guid groupId, CancellationToken ct = default)
            => throw new InvalidOperationException("Sensitive internal error message");
    }

    private class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Logs { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Logs.Add((logLevel, exception, formatter(state, exception)));
        }
    }
}
