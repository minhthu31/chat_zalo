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

/// <summary>
/// Mã lỗi phân quyền theo docs/API_CONTRACT.md v1.2 (Sprint 2): body lỗi { "code", "message" }.
/// Nhiều tình huống dùng chung GROUP_ACCESS_DENIED vì hợp đồng gộp "thiếu quyền" vào một mã.
/// </summary>
public static class GroupPermissionErrors
{
    public const string NotAMember = "GROUP_ACCESS_DENIED";        // 403
    public const string LeftGroup = "GROUP_ACCESS_DENIED";         // 403 (đã rời hoặc bị xóa)
    public const string RemovedFromGroup = "GROUP_ACCESS_DENIED";  // 403
    public const string GroupDisbanded = "GROUP_ACCESS_DENIED";    // 403
    public const string AdminRequired = "GROUP_ACCESS_DENIED";     // 403
    public const string NotFriends = "NOT_FRIEND";                 // 403
    public const string AlreadyMember = "ALREADY_GROUP_MEMBER";    // 409
    public const string TargetNotMember = "NOT_FOUND";             // 404
    public const string InvalidTarget = "VALIDATION_ERROR";        // 400

    /// <summary>HTTP status tương ứng với mã lỗi theo hợp đồng.</summary>
    public static int ToHttpStatus(string code) => code switch
    {
        "GROUP_ACCESS_DENIED" or "NOT_FRIEND" => 403,
        "ALREADY_GROUP_MEMBER" => 409,
        "NOT_FOUND" => 404,
        "VALIDATION_ERROR" => 400,
        _ => 403
    };
}

/// <summary>Kết quả kiểm tra quyền. Allowed = true thì ActorRole luôn có giá trị.</summary>
public sealed record PermissionResult(bool Allowed, string? ErrorCode, string? Message, GroupRole? ActorRole)
{
    /// <summary>HTTP status theo hợp đồng khi bị từ chối; null nếu được phép.</summary>
    public int? HttpStatus => ErrorCode is null ? null : GroupPermissionErrors.ToHttpStatus(ErrorCode);

    public static PermissionResult Allow(GroupRole role) => new(true, null, null, role);
    public static PermissionResult Deny(string code, string message) => new(false, code, message, null);

    /// <summary>Dùng trong Service khác: ném exception nếu bị từ chối.</summary>
    public PermissionResult ThrowIfDenied()
        => Allowed ? this : throw new GroupPermissionException(ErrorCode!, Message!);
}

public sealed class GroupPermissionException : Exception
{
    public string ErrorCode { get; }
    public int HttpStatus => GroupPermissionErrors.ToHttpStatus(ErrorCode);

    public GroupPermissionException(string errorCode, string message) : base(message)
        => ErrorCode = errorCode;
}