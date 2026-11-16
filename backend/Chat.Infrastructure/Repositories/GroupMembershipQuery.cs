using Chat.Application.Interfaces;
using Chat.Application.Models.Groups;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Repositories;

/// <summary>
/// Đọc quan hệ thành viên nhóm từ ConversationMembers + GroupConversations.
/// - Là nhóm: có dòng GroupConversation (chat 1-1 không có nên không bao giờ khớp).
/// - Nhóm hoạt động: GroupConversation.DeletedAt == null.
/// - Thành viên hoạt động: ConversationMember.LeftAt == null.
/// - Role "Admin" hoặc "Owner" được coi là Admin.
/// Schema chỉ có LeftAt nên "đã rời" và "bị xóa" đều trả về trạng thái Left.
/// </summary>
public sealed class GroupMembershipQuery : IGroupMembershipQuery
{
    private readonly ChatDbContext _db;

    public GroupMembershipQuery(ChatDbContext db) => _db = db;

    public async Task<GroupMembershipSnapshot?> GetMembershipAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var row = await (
            from m in _db.Set<ConversationMember>().AsNoTracking()
            join g in _db.Set<GroupConversation>().AsNoTracking() on m.ConversationId equals g.ConversationId
            where m.ConversationId == groupId && m.UserId == userId
            select new { m.Role, m.LeftAt, g.DeletedAt })
            .SingleOrDefaultAsync(ct);

        if (row is null) return null;

        return new GroupMembershipSnapshot(
            groupId,
            userId,
            row.Role is "Admin" or "Owner" ? GroupRole.Admin : GroupRole.Member,
            row.LeftAt is null ? GroupMemberStatus.Active : GroupMemberStatus.Left,
            row.DeletedAt is null);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveGroupIdsAsync(Guid userId, CancellationToken ct = default)
    {
        return await (
            from m in _db.Set<ConversationMember>().AsNoTracking()
            join g in _db.Set<GroupConversation>().AsNoTracking() on m.ConversationId equals g.ConversationId
            where m.UserId == userId && m.LeftAt == null && g.DeletedAt == null
            select m.ConversationId)
            .ToListAsync(ct);
    }
}
