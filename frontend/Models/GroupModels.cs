namespace Chat.Client.Models;

// Các kiểu dữ liệu của nhóm, đúng theo hợp đồng Sprint 2 (mục 9).

// Hai giá trị của GroupMemberDto.Role
public static class GroupRoles
{
    public const string Admin = "Admin";
    public const string Member = "Member";
}

// Trạng thái của một đề nghị chuyển Admin (AdminTransferDto.Status)
public static class AdminTransferStatuses
{
    public const string Pending = "Pending";
}

public class GroupDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "Group";
    public string Name { get; set; } = "";
    public long LastMessageSeq { get; set; }
}

public class GroupMemberDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = GroupRoles.Member;

    public bool IsAdmin => Role == GroupRoles.Admin;
}

public class AdminTransferDto
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public string Status { get; set; } = AdminTransferStatuses.Pending;
    public DateTimeOffset CreatedAt { get; set; }
}

// Body của POST /api/conversations/groups
public class CreateGroupRequest
{
    public string Name { get; set; } = "";
    public List<Guid> MemberIds { get; set; } = new();
}

// Body của POST /api/conversations/{id}/members
public class AddGroupMemberRequest
{
    public Guid UserId { get; set; }
}

// Body của POST /api/conversations/{id}/admin-transfer
public class AdminTransferRequest
{
    public Guid TargetUserId { get; set; }
}
