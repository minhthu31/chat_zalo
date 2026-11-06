using Chat.Client.Models;
using Chat.Client.Services;

namespace Chat.Client.State;

// Giữ "ai đang đăng nhập" cho cả ứng dụng. Các trang hỏi AuthState thay vì tự gọi API.
public class AuthState
{
    private readonly IAuthApiClient _authApi;

    public AuthState(IAuthApiClient authApi)
    {
        _authApi = authApi;
    }

    public UserResponse? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    // false cho tới khi hỏi server xong lần đầu, để trang biết đang "chờ" chứ chưa phải "chưa đăng nhập"
    public bool IsInitialized { get; private set; }

    // Các component đăng ký vào đây để được báo khi trạng thái đổi và tự vẽ lại giao diện
    public event Action? OnChange;

    // Gọi khi mở app / F5: hỏi server xem cookie còn hiệu lực không (GET /api/auth/me)
    public async Task InitializeAsync()
    {
        try
        {
            CurrentUser = await _authApi.GetMeAsync();
        }
        finally
        {
            IsInitialized = true;
            NotifyStateChanged();
        }
    }

    // Lỗi từ server (ApiException, ví dụ INVALID_CREDENTIALS) được để nổi lên cho trang Login xử lý theo Code
    public async Task LoginAsync(LoginRequest request)
    {
        CurrentUser = await _authApi.LoginAsync(request);
        NotifyStateChanged();
    }

    // Dù server báo lỗi vẫn xóa user ở phía FE, để giao diện không còn hiện là đang đăng nhập
    public async Task LogoutAsync()
    {
        try
        {
            await _authApi.LogoutAsync();
        }
        finally
        {
            CurrentUser = null;
            NotifyStateChanged();
        }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
