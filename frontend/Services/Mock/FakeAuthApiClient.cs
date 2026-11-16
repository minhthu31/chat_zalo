using Chat.Client.Models;

namespace Chat.Client.Services.Mock;

public class FakeAuthApiClient : IAuthApiClient
{
    private record FakeAccount(UserResponse User, string Email, string Password);

    private readonly List<FakeAccount> _accounts = new()
    {
        new(new UserResponse { Id = Guid.NewGuid(), Username = "user01", DisplayName = "Nguyen Van A" },
            "user01@example.com", "Password123!")
    };

    private UserResponse? _current;

    public async Task<UserResponse> RegisterAsync(RegisterRequest request)
    {
        await Task.Delay(500); // giả vờ chờ mạng

        bool trung = _accounts.Any(a =>
            a.User.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase) ||
            a.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase));

        if (trung)
            throw new ApiException(409, "CONFLICT", "Username or email already exists");

        var user = new UserResponse
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            DisplayName = request.Username
        };
        _accounts.Add(new FakeAccount(user, request.Email, request.Password));
        return user; // theo hợp đồng: đăng ký xong KHÔNG tự đăng nhập
    }

    public async Task<UserResponse> LoginAsync(LoginRequest request)
    {
        await Task.Delay(500);

        var nhap = request.UsernameOrEmail.Trim();
        var acc = _accounts.FirstOrDefault(a =>
            (a.User.Username.Equals(nhap, StringComparison.OrdinalIgnoreCase) ||
             a.Email.Equals(nhap, StringComparison.OrdinalIgnoreCase))
            && a.Password == request.Password);

        if (acc is null)
            throw new ApiException(401, "INVALID_CREDENTIALS", "Invalid username or password");

        _current = acc.User;
        return acc.User;
    }

    public Task LogoutAsync()
    {
        _current = null;
        return Task.CompletedTask;
    }

    public Task<UserResponse?> GetMeAsync() => Task.FromResult(_current);
}