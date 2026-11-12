namespace Chat.Application.Models.Groups;

/// <summary>Vai trò của thành viên trong nhóm (DATABASE_RULES: group_members.role).</summary>
public enum GroupRole
{
    Member = 0,
    Admin = 1
}

/// <summary>Trạng thái thành viên: đang ở trong nhóm, đã tự rời, hoặc bị Admin xóa.</summary>
public enum GroupMemberStatus
{
    Active = 0,
    Left = 1,
    Removed = 2
}

/// <summary>Ảnh chụp (snapshot) quan hệ thành viên - nhóm đọc từ DB tại thời điểm kiểm tra.</summary>
public sealed record GroupMembershipSnapshot(
    Guid GroupId,
    Guid UserId,
    GroupRole Role,
    GroupMemberStatus Status,
    bool GroupIsActive);

/// <summary>Mã lỗi phân quyền - map sang error code trong API_CONTRACT.</summary>
public static class GroupPermissionErrors
{
    public const string NotAMember = "GROUP_NOT_A_MEMBER";
    public const string LeftGroup = "GROUP_LEFT";
    public const string RemovedFromGroup = "GROUP_REMOVED";
    public const string GroupDisbanded = "GROUP_DISBANDED";
    public const string AdminRequired = "GROUP_ADMIN_REQUIRED";
    public const string InvalidTarget = "GROUP_INVALID_TARGET";
    public const string TargetNotMember = "GROUP_TARGET_NOT_MEMBER";
    public const string AlreadyMember = "GROUP_ALREADY_MEMBER";
    public const string NotFriends = "GROUP_NOT_FRIENDS";
}

/// <summary>Kết quả kiểm tra quyền. Allowed = true thì ActorRole luôn có giá trị.</summary>
public sealed record PermissionResult(bool Allowed, string? ErrorCode, string? Message, GroupRole? ActorRole)
{
    public static PermissionResult Allow(GroupRole role) => new(true, null, null, role);
    public static PermissionResult Deny(string code, string message) => new(false, code, message, null);

    /// <summary>Dùng trong Service khác: ném exception nếu bị từ chối.</summary>
    public PermissionResult ThrowIfDenied()
        => Allowed ? this : throw new GroupPermissionException(ErrorCode!, Message!);
}

public sealed class GroupPermissionException : Exception
{
    public string ErrorCode { get; }

    public GroupPermissionException(string errorCode, string message) : base(message)
        => ErrorCode = errorCode;
}
