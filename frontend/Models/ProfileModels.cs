namespace Chat.Client.Models;

// ProfileDto của API hồ sơ Sprint 3. Không có Email (hợp đồng cấm công khai).
public class ProfileDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Bio { get; set; } = "";

    // Có ảnh: /api/users/{id}/avatar; chưa có ảnh: null
    public string? AvatarUrl { get; set; }
}

// Body của PUT /api/users/me/profile. Cập nhật CẢ HAI trường; bio rỗng nghĩa là xóa Bio.
public class SaveProfileRequest
{
    public string DisplayName { get; set; } = "";
    public string Bio { get; set; } = "";
}

// Giới hạn theo hợp đồng Sprint 3 (mục 2), dùng để kiểm tra sớm ở phía FE
public static class ProfileLimits
{
    public const int DisplayNameMax = 50;
    public const int BioMax = 200;

    // 2 MB theo quy ước hợp đồng: 2.097.152 byte
    public const long AvatarMaxBytes = 2 * 1024 * 1024;

    public static readonly string[] AvatarContentTypes = { "image/jpeg", "image/png", "image/webp" };
}
