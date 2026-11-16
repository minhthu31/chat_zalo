using Chat.Application.Models.Groups;

namespace Chat.Application.Interfaces;

/// <summary>
/// Dịch vụ kiểm tra quyền nhóm DÙNG CHUNG cho Conversations (S2-06), Messages (S2-07)
/// và SignalR (S2-08). Chỉ KIỂM TRA quyền, không thực hiện thao tác quản lý nhóm.
/// Mọi phương thức đọc dữ liệu mới nhất từ DB (không cache) để quyền thay đổi có hiệu lực ngay.
/// </summary>
public interface IGroupPermissionService
{
    // ----- Thành viên / vai trò -----
    /// <summary>Người dùng là thành viên ACTIVE của nhóm ACTIVE (chưa giải tán)?</summary>
    Task<PermissionResult> GetAccessAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    Task<bool> IsActiveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<bool> IsAdminAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<GroupRole?> GetRoleAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    // ----- Thao tác quản lý (chỉ kiểm tra quyền; S2-06 thực thi) -----
    /// <summary>Chỉ Admin được xóa thành viên KHÁC.</summary>
    Task<PermissionResult> CanRemoveMemberAsync(Guid groupId, Guid actorId, Guid targetUserId, CancellationToken ct = default);

    /// <summary>Chỉ Admin được giải tán nhóm.</summary>
    Task<PermissionResult> CanDisbandGroupAsync(Guid groupId, Guid actorId, CancellationToken ct = default);

    /// <summary>Thành viên (Admin/Member) được thêm BẠN BÈ của chính mình (theo Friendships - S2-04).</summary>
    Task<PermissionResult> CanAddMemberAsync(Guid groupId, Guid actorId, Guid targetUserId, CancellationToken ct = default);

    /// <summary>Chỉ Admin được đề nghị chuyển quyền quản trị cho một thành viên active khác.</summary>
    Task<PermissionResult> CanProposeAdminTransferAsync(Guid groupId, Guid actorId, Guid targetUserId, CancellationToken ct = default);

    // ----- Tin nhắn -----
    Task<PermissionResult> CanSendMessageAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<PermissionResult> CanViewMessagesAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    // ----- SignalR -----
    /// <summary>Kiểm tra khi client Join group trên hub /hubs/chat.</summary>
    Task<PermissionResult> CanJoinRealtimeAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    /// <summary>Kiểm tra trước khi đẩy sự kiện realtime tới user (đã rời/bị xóa thì không nhận).</summary>
    Task<PermissionResult> CanReceiveRealtimeAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    /// <summary>Danh sách nhóm active của user - dùng khi OnConnectedAsync tự Join các group.</summary>
    Task<IReadOnlyList<Guid>> GetRealtimeGroupIdsAsync(Guid userId, CancellationToken ct = default);
}
