using Chat.Client.Models;

namespace Chat.Client.Services;

// Nhớ hồ sơ đã tải trong phiên làm việc: một trang có thể hiện hàng chục người (tìm kiếm, bạn bè, thành viên nhóm),
// mỗi người chỉ cần hỏi backend một lần. Khi hồ sơ đổi (đổi ảnh, đổi tên) thì gọi Invalidate để mọi nơi tải lại.
public class ProfileCache
{
    private readonly IProfileApiClient _api;
    private readonly Dictionary<Guid, Task<ProfileDto?>> _cache = new();
    private readonly Dictionary<Guid, int> _versions = new();

    public ProfileCache(IProfileApiClient api)
    {
        _api = api;
    }

    // Các component đang hiện hồ sơ đăng ký vào đây để vẽ lại khi hồ sơ của ai đó đổi
    public event Action<Guid>? Changed;

    // Trả null nếu không tải được (mạng lỗi, không tìm thấy) để giao diện tự dùng chữ cái đầu thay cho ảnh
    public Task<ProfileDto?> GetAsync(Guid userId)
    {
        if (_cache.TryGetValue(userId, out var cached))
            return cached;

        var task = LoadAsync(userId);
        _cache[userId] = task;
        return task;
    }

    // Số tăng mỗi lần hồ sơ đổi: gắn vào địa chỉ ảnh (?v=) để trình duyệt không dùng lại ảnh cũ
    public int VersionOf(Guid userId) => _versions.GetValueOrDefault(userId);

    public void Invalidate(Guid userId)
    {
        _cache.Remove(userId);
        _versions[userId] = VersionOf(userId) + 1;
        Changed?.Invoke(userId);
    }

    private async Task<ProfileDto?> LoadAsync(Guid userId)
    {
        try
        {
            return await _api.GetAsync(userId);
        }
        catch (ApiException)
        {
            _cache.Remove(userId); // lỗi tạm thời thì lần sau hỏi lại, không nhớ lỗi
            return null;
        }
    }
}
