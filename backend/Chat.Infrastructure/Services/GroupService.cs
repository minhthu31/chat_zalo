using Chat.Application.DTOs.Groups;
using Chat.Application.Interfaces;
using Chat.Application.Models.Groups;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Services;

public class GroupService : IGroupService
{
    private readonly ChatDbContext _db;
    private readonly IFriendshipService _friendships;
    private readonly IGroupPermissionService _groupPermissions;

    public GroupService(
        ChatDbContext db,
        IFriendshipService friendships,
        IGroupPermissionService groupPermissions)
    {
        _db = db;
        _friendships = friendships;
        _groupPermissions = groupPermissions;
    }

    public async Task<GroupDto> CreateGroupAsync(Guid currentUserId, CreateGroupRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
        {
            throw new GroupServiceException(400, "VALIDATION_ERROR", "Tên nhóm không được để trống và tối đa 100 ký tự.");
        }

        if (request.MemberIds == null || request.MemberIds.Count == 0)
        {
            throw new GroupServiceException(400, "VALIDATION_ERROR", "Nhóm phải có ít nhất một thành viên ban đầu.");
        }

        if (request.MemberIds.Distinct().Count() != request.MemberIds.Count)
        {
            throw new GroupServiceException(400, "VALIDATION_ERROR", "Danh sách thành viên không được chứa ID trùng lặp.");
        }

        if (request.MemberIds.Contains(currentUserId))
        {
            throw new GroupServiceException(400, "VALIDATION_ERROR", "Không được thêm chính mình vào danh sách thành viên được mời.");
        }

        if (request.MemberIds.Any(id => id == Guid.Empty))
        {
            throw new GroupServiceException(400, "VALIDATION_ERROR", "ID thành viên không hợp lệ.");
        }

        // Kiểm tra tất cả thành viên được mời: phải tồn tại và là bạn bè của người tạo
        foreach (var memberId in request.MemberIds)
        {
            var exists = await _db.Users.AsNoTracking().AnyAsync(u => u.Id == memberId, ct);
            if (!exists)
            {
                throw new GroupServiceException(404, "NOT_FOUND", $"Người dùng {memberId} không tồn tại.");
            }

            var areFriends = await _friendships.AreFriendsAsync(currentUserId, memberId);
            if (!areFriends)
            {
                throw new GroupServiceException(403, "NOT_FRIEND", "Chỉ được mời bạn bè của chính mình vào nhóm.");
            }
        }

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            var now = DateTimeOffset.UtcNow;
            var conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                CreatedByUserId = currentUserId,
                Kind = "Group",
                LastMessageSeq = 0L,
                CreatedAt = now,
                LastActivityAt = now
            };

            var groupConversation = new GroupConversation
            {
                ConversationId = conversation.Id,
                Name = request.Name.Trim(),
                DeletedAt = null
            };

            var creatorMember = new ConversationMember
            {
                ConversationId = conversation.Id,
                UserId = currentUserId,
                Role = "Admin",
                JoinedSeq = 0L,
                JoinedAt = now,
                LastReadSeq = 0L,
                LastDeliveredSeq = 0L
            };

            _db.Conversations.Add(conversation);
            _db.GroupConversations.Add(groupConversation);
            _db.ConversationMembers.Add(creatorMember);

            foreach (var memberId in request.MemberIds)
            {
                _db.ConversationMembers.Add(new ConversationMember
                {
                    ConversationId = conversation.Id,
                    UserId = memberId,
                    Role = "Member",
                    JoinedSeq = 0L,
                    JoinedAt = now,
                    LastReadSeq = 0L,
                    LastDeliveredSeq = 0L
                });
            }

            await _db.SaveChangesAsync(ct);

            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }

            return new GroupDto
            {
                Id = conversation.Id,
                Kind = "Group",
                Name = groupConversation.Name,
                LastMessageSeq = 0L
            };
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(ct);
            }
            throw;
        }
    }

    public async Task<GroupDto> GetGroupAsync(Guid currentUserId, Guid groupId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .AsNoTracking()
            .Include(c => c.GroupConversation)
            .FirstOrDefaultAsync(c => c.Id == groupId, ct);

        if (conversation == null || conversation.Kind != "Group" || conversation.GroupConversation == null)
        {
            throw new GroupServiceException(400, "INVALID_CONVERSATION_KIND", "Hội thoại không phải là nhóm.");
        }

        if (conversation.GroupConversation.DeletedAt != null)
        {
            throw new GroupServiceException(403, "GROUP_ACCESS_DENIED", "Nhóm đã được giải tán.");
        }

        var isMember = await _groupPermissions.IsActiveMemberAsync(groupId, currentUserId, ct);
        if (!isMember)
        {
            throw new GroupServiceException(403, "GROUP_ACCESS_DENIED", "Bạn không thuộc nhóm này.");
        }

        return new GroupDto
        {
            Id = conversation.Id,
            Kind = "Group",
            Name = conversation.GroupConversation.Name,
            LastMessageSeq = conversation.LastMessageSeq
        };
    }

    public async Task<List<GroupMemberDto>> GetMembersAsync(Guid currentUserId, Guid groupId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .AsNoTracking()
            .Include(c => c.GroupConversation)
            .FirstOrDefaultAsync(c => c.Id == groupId, ct);

        if (conversation == null || conversation.Kind != "Group" || conversation.GroupConversation == null)
        {
            throw new GroupServiceException(400, "INVALID_CONVERSATION_KIND", "Hội thoại không phải là nhóm.");
        }

        if (conversation.GroupConversation.DeletedAt != null)
        {
            throw new GroupServiceException(403, "GROUP_ACCESS_DENIED", "Nhóm đã được giải tán.");
        }

        var isMember = await _groupPermissions.IsActiveMemberAsync(groupId, currentUserId, ct);
        if (!isMember)
        {
            throw new GroupServiceException(403, "GROUP_ACCESS_DENIED", "Bạn không thuộc nhóm này.");
        }

        var members = await (
            from m in _db.ConversationMembers.AsNoTracking()
            join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
            where m.ConversationId == groupId && m.LeftAt == null
            orderby m.Role == "Admin" ? 0 : 1, m.JoinedAt
            select new GroupMemberDto
            {
                UserId = m.UserId,
                Username = u.UserName ?? string.Empty,
                DisplayName = u.DisplayName ?? string.Empty,
                Role = m.Role
            })
            .ToListAsync(ct);

        return members;
    }

    public async Task<GroupMemberDto> AddMemberAsync(Guid currentUserId, Guid groupId, Guid targetUserId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .Include(c => c.GroupConversation)
            .FirstOrDefaultAsync(c => c.Id == groupId, ct);

        if (conversation == null || conversation.Kind != "Group" || conversation.GroupConversation == null)
        {
            throw new GroupServiceException(400, "INVALID_CONVERSATION_KIND", "Hội thoại không phải là nhóm.");
        }

        var permissionResult = await _groupPermissions.CanAddMemberAsync(groupId, currentUserId, targetUserId, ct);
        permissionResult.ThrowIfDenied();

        var targetUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (targetUser == null)
        {
            throw new GroupServiceException(404, "NOT_FOUND", "Người dùng không tồn tại.");
        }

        var existingMember = await _db.ConversationMembers
            .FirstOrDefaultAsync(m => m.ConversationId == groupId && m.UserId == targetUserId, ct);

        if (existingMember != null)
        {
            existingMember.LeftAt = null;
            existingMember.JoinedAt = DateTimeOffset.UtcNow;
            existingMember.Role = "Member";
        }
        else
        {
            var newMember = new ConversationMember
            {
                ConversationId = groupId,
                UserId = targetUserId,
                Role = "Member",
                JoinedSeq = conversation.LastMessageSeq,
                JoinedAt = DateTimeOffset.UtcNow,
                LastReadSeq = 0L,
                LastDeliveredSeq = 0L
            };
            _db.ConversationMembers.Add(newMember);
        }

        conversation.LastActivityAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new GroupMemberDto
        {
            UserId = targetUser.Id,
            Username = targetUser.UserName ?? string.Empty,
            DisplayName = targetUser.DisplayName ?? string.Empty,
            Role = "Member"
        };
    }

    public async Task RemoveMemberAsync(Guid currentUserId, Guid groupId, Guid targetUserId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .Include(c => c.GroupConversation)
            .FirstOrDefaultAsync(c => c.Id == groupId, ct);

        if (conversation == null || conversation.Kind != "Group" || conversation.GroupConversation == null)
        {
            throw new GroupServiceException(400, "INVALID_CONVERSATION_KIND", "Hội thoại không phải là nhóm.");
        }

        var permissionResult = await _groupPermissions.CanRemoveMemberAsync(groupId, currentUserId, targetUserId, ct);
        permissionResult.ThrowIfDenied();

        var member = await _db.ConversationMembers
            .FirstOrDefaultAsync(m => m.ConversationId == groupId && m.UserId == targetUserId && m.LeftAt == null, ct);

        if (member == null)
        {
            throw new GroupServiceException(404, "NOT_FOUND", "Thành viên không tồn tại trong nhóm.");
        }

        member.LeftAt = DateTimeOffset.UtcNow;
        conversation.LastActivityAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<AdminTransferDto> ProposeAdminTransferAsync(Guid currentUserId, Guid groupId, Guid targetUserId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .Include(c => c.GroupConversation)
            .FirstOrDefaultAsync(c => c.Id == groupId, ct);

        if (conversation == null || conversation.Kind != "Group" || conversation.GroupConversation == null)
        {
            throw new GroupServiceException(400, "INVALID_CONVERSATION_KIND", "Hội thoại không phải là nhóm.");
        }

        var permissionResult = await _groupPermissions.CanProposeAdminTransferAsync(groupId, currentUserId, targetUserId, ct);
        permissionResult.ThrowIfDenied();

        var hasPending = await _db.GroupAdminTransferRequests
            .AnyAsync(r => r.ConversationId == groupId && r.Status == "Pending", ct);

        if (hasPending)
        {
            throw new GroupServiceException(409, "ADMIN_TRANSFER_PENDING", "Đang có yêu cầu chuyển quyền Admin chờ xử lý.");
        }

        var request = new GroupAdminTransferRequest
        {
            Id = Guid.NewGuid(),
            ConversationId = groupId,
            FromUserId = currentUserId,
            ToUserId = targetUserId,
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
            ResolvedAt = null
        };

        _db.GroupAdminTransferRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        return new AdminTransferDto
        {
            Id = request.Id,
            ConversationId = request.ConversationId,
            FromUserId = request.FromUserId,
            ToUserId = request.ToUserId,
            Status = request.Status,
            CreatedAt = request.CreatedAt
        };
    }

    public async Task AcceptAdminTransferAsync(Guid currentUserId, Guid groupId, Guid requestId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .Include(c => c.GroupConversation)
            .FirstOrDefaultAsync(c => c.Id == groupId, ct);

        if (conversation == null || conversation.Kind != "Group" || conversation.GroupConversation == null)
        {
            throw new GroupServiceException(400, "INVALID_CONVERSATION_KIND", "Hội thoại không phải là nhóm.");
        }

        if (conversation.GroupConversation.DeletedAt != null)
        {
            throw new GroupServiceException(403, "GROUP_ACCESS_DENIED", "Nhóm đã được giải tán.");
        }

        var request = await _db.GroupAdminTransferRequests
            .FirstOrDefaultAsync(r => r.Id == requestId && r.ConversationId == groupId, ct);

        if (request == null)
        {
            throw new GroupServiceException(404, "NOT_FOUND", "Yêu cầu chuyển quyền Admin không tồn tại.");
        }

        if (request.ToUserId != currentUserId)
        {
            throw new GroupServiceException(403, "GROUP_ACCESS_DENIED", "Chỉ người nhận quyền mới có thể chấp nhận yêu cầu.");
        }

        if (request.Status != "Pending")
        {
            throw new GroupServiceException(409, "ADMIN_TRANSFER_ALREADY_RESOLVED", "Yêu cầu này đã được xử lý.");
        }

        var nominee = await _db.ConversationMembers
            .FirstOrDefaultAsync(m => m.ConversationId == groupId && m.UserId == currentUserId && m.LeftAt == null, ct);

        if (nominee == null)
        {
            throw new GroupServiceException(404, "NOT_FOUND", "Người nhận quyền không còn là thành viên hoạt động.");
        }

        var oldAdmin = await _db.ConversationMembers
            .FirstOrDefaultAsync(m => m.ConversationId == groupId && m.UserId == request.FromUserId && m.LeftAt == null, ct);

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            // 1. Hạ quyền (demote) Admin cũ xuống làm Member TRƯỚC và lưu lại để tránh vi phạm unique index IX_ConversationMembers_OneActiveAdmin
            if (oldAdmin != null)
            {
                oldAdmin.Role = "Member";
                await _db.SaveChangesAsync(ct);
            }

            // 2. Nâng quyền (promote) người mới lên làm Admin và cập nhật trạng thái yêu cầu
            nominee.Role = "Admin";
            request.Status = "Accepted";
            request.ResolvedAt = DateTimeOffset.UtcNow;
            conversation.LastActivityAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            // 3. Cam kết thay đổi (Commit)
            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(ct);
            }
            throw;
        }
    }

    public async Task RejectAdminTransferAsync(Guid currentUserId, Guid groupId, Guid requestId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .Include(c => c.GroupConversation)
            .FirstOrDefaultAsync(c => c.Id == groupId, ct);

        if (conversation == null || conversation.Kind != "Group" || conversation.GroupConversation == null)
        {
            throw new GroupServiceException(400, "INVALID_CONVERSATION_KIND", "Hội thoại không phải là nhóm.");
        }

        var request = await _db.GroupAdminTransferRequests
            .FirstOrDefaultAsync(r => r.Id == requestId && r.ConversationId == groupId, ct);

        if (request == null)
        {
            throw new GroupServiceException(404, "NOT_FOUND", "Yêu cầu chuyển quyền Admin không tồn tại.");
        }

        if (request.ToUserId != currentUserId)
        {
            throw new GroupServiceException(403, "GROUP_ACCESS_DENIED", "Chỉ người nhận quyền mới có thể từ chối yêu cầu.");
        }

        if (request.Status != "Pending")
        {
            throw new GroupServiceException(409, "ADMIN_TRANSFER_ALREADY_RESOLVED", "Yêu cầu này đã được xử lý.");
        }

        request.Status = "Rejected";
        request.ResolvedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task LeaveGroupAsync(Guid currentUserId, Guid groupId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .Include(c => c.GroupConversation)
            .FirstOrDefaultAsync(c => c.Id == groupId, ct);

        if (conversation == null || conversation.Kind != "Group" || conversation.GroupConversation == null)
        {
            throw new GroupServiceException(400, "INVALID_CONVERSATION_KIND", "Hội thoại không phải là nhóm.");
        }

        if (conversation.GroupConversation.DeletedAt != null)
        {
            throw new GroupServiceException(403, "GROUP_ACCESS_DENIED", "Nhóm đã được giải tán.");
        }

        var member = await _db.ConversationMembers
            .FirstOrDefaultAsync(m => m.ConversationId == groupId && m.UserId == currentUserId && m.LeftAt == null, ct);

        if (member == null)
        {
            throw new GroupServiceException(403, "GROUP_ACCESS_DENIED", "Bạn không thuộc nhóm này.");
        }

        if (member.Role == "Admin")
        {
            var otherActiveCount = await _db.ConversationMembers
                .CountAsync(m => m.ConversationId == groupId && m.UserId != currentUserId && m.LeftAt == null, ct);

            if (otherActiveCount > 0)
            {
                throw new GroupServiceException(400, "VALIDATION_ERROR", "Admin phải chuyển quyền quản trị trước khi rời nhóm.");
            }

            // Nhóm chỉ còn Admin duy nhất -> giải tán nhóm
            conversation.GroupConversation.DeletedAt = DateTimeOffset.UtcNow;
            member.LeftAt = DateTimeOffset.UtcNow;
        }
        else
        {
            member.LeftAt = DateTimeOffset.UtcNow;
        }

        conversation.LastActivityAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DisbandGroupAsync(Guid currentUserId, Guid groupId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .Include(c => c.GroupConversation)
            .FirstOrDefaultAsync(c => c.Id == groupId, ct);

        if (conversation == null || conversation.Kind != "Group" || conversation.GroupConversation == null)
        {
            throw new GroupServiceException(400, "INVALID_CONVERSATION_KIND", "Hội thoại không phải là nhóm.");
        }

        var permissionResult = await _groupPermissions.CanDisbandGroupAsync(groupId, currentUserId, ct);
        permissionResult.ThrowIfDenied();

        if (conversation.GroupConversation.DeletedAt != null)
        {
            throw new GroupServiceException(404, "NOT_FOUND", "Nhóm đã được giải tán trước đó.");
        }

        conversation.GroupConversation.DeletedAt = DateTimeOffset.UtcNow;
        conversation.LastActivityAt = DateTimeOffset.UtcNow;

        var pendingRequests = await _db.GroupAdminTransferRequests
            .Where(r => r.ConversationId == groupId && r.Status == "Pending")
            .ToListAsync(ct);

        foreach (var req in pendingRequests)
        {
            req.Status = "Cancelled";
            req.ResolvedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}
