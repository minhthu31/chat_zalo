using Chat.Client.Models;

namespace Chat.Client.Services;

// Nhớ trạng thái tắt thông báo của từng hội thoại để mỗi tin mới không phải hỏi backend lại.
// Hợp đồng: IsMuted chỉ tắt âm thanh và banner của FE; tin vẫn được nhận và lưu bình thường.
public class NotificationService
{
    private readonly IConversationSettingsApiClient _api;
    private readonly Dictionary<Guid, bool> _muted = new();

    public NotificationService(IConversationSettingsApiClient api)
    {
        _api = api;
    }

    // Các nút bật/tắt đang hiển thị đăng ký vào đây để đổi theo khi trạng thái thay đổi
    public event Action<Guid>? Changed;

    // Không đọc được từ backend thì coi như chưa tắt (thà báo thừa còn hơn lỡ tin)
    public async Task<bool> IsMutedAsync(Guid conversationId)
    {
        if (_muted.TryGetValue(conversationId, out var cached))
            return cached;

        try
        {
            var settings = await _api.GetNotificationSettingsAsync(conversationId);
            _muted[conversationId] = settings.IsMuted;
            return settings.IsMuted;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    public async Task SetMutedAsync(Guid conversationId, bool muted)
    {
        var settings = await _api.SetMutedAsync(conversationId, muted);
        _muted[conversationId] = settings.IsMuted;
        Changed?.Invoke(conversationId);
    }

    // Đọc lại từ backend (sau khi kết nối lại, hoặc đổi trên thiết bị khác)
    public async Task RefreshAsync(Guid conversationId)
    {
        _muted.Remove(conversationId);
        await IsMutedAsync(conversationId);
        Changed?.Invoke(conversationId);
    }

    public void Forget() => _muted.Clear();
}
