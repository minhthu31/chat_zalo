using Chat.Application.Models.Groups;

namespace Chat.Application.Interfaces;

/// <summary>Query read-only hỗ trợ kiểm tra vai trò và thành viên (triển khai bằng EF Core).</summary>
public interface IGroupMembershipQuery
{
    /// <summary>Trả về null nếu nhóm không tồn tại hoặc user chưa từng là thành viên.</summary>
    Task<GroupMembershipSnapshot?> GetMembershipAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    /// <summary>Id các nhóm ACTIVE mà user đang là thành viên ACTIVE.</summary>
    Task<IReadOnlyList<Guid>> GetActiveGroupIdsAsync(Guid userId, CancellationToken ct = default);
}
