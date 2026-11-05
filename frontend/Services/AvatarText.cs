namespace Chat.Client.Services;

// Lấy chữ cái đầu của tên để hiển thị trong ảnh đại diện tròn
public static class AvatarText
{
    public static string From(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? "?" : trimmed[..1].ToUpperInvariant();
    }
}
