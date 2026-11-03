using Xunit;
using Chat.Application.DTOs.Users;
using Chat.Application.Interfaces;
using Chat.Application.Services;

namespace Chat.Users.Tests;

// ---------------------------------------------------------------------------
// CHAT-03: Stub IUserAccountGateway - test UserService khong can database.
// ---------------------------------------------------------------------------
internal sealed class FakeUserAccountGateway : IUserAccountGateway
{
    private readonly Dictionary<string, UserDto> _store = new();

    public void Seed(string id, string username, string displayName)
    {
        _store[id] = new UserDto { Id = id, Username = username, DisplayName = displayName };
    }

    public Task<UserDto?> FindByIdAsync(string userId)
    {
        _store.TryGetValue(userId, out var dto);
        return Task.FromResult(dto);
    }

    public Task<UserDto?> UpdateDisplayNameAsync(string userId, string displayName)
    {
        if (!_store.TryGetValue(userId, out var dto))
            return Task.FromResult<UserDto?>(null);
        var updated = new UserDto { Id = dto.Id, Username = dto.Username, DisplayName = displayName };
        _store[userId] = updated;
        return Task.FromResult<UserDto?>(updated);
    }

    public Task<IReadOnlyList<UserDto>> SearchByUsernameAsync(string username)
    {
        IReadOnlyList<UserDto> results = _store.Values
            .Where(u => u.Username.Contains(username, StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.Username)
            .Take(20)
            .ToList();
        return Task.FromResult(results);
    }
}

// ===========================================================================
// CHAT-03: Unit Tests cho UserService (IUserService)
// Bao phu: GetProfile, UpdateProfile, Search - theo API_CONTRACT v1.2
// ===========================================================================
public sealed class UserServiceTests
{
    private readonly FakeUserAccountGateway _gateway;
    private readonly UserService _sut;

    private static readonly Guid ExistingUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MissingUserId  = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public UserServiceTests()
    {
        _gateway = new FakeUserAccountGateway();
        _gateway.Seed(ExistingUserId.ToString(), "alice", "Alice Nguyen");
        _gateway.Seed("22222222-2222-2222-2222-222222222222", "alice_b", "Alice B");
        _gateway.Seed("33333333-3333-3333-3333-333333333333", "bob", "Bob Tran");
        _sut = new UserService(_gateway);
    }

    // === GetProfileAsync ===

    [Fact]
    public async Task GetProfile_ExistingUser_ReturnsCorrectDto()
    {
        var result = await _sut.GetProfileAsync(ExistingUserId);
        Assert.Equal(ExistingUserId.ToString(), result.Id);
        Assert.Equal("alice", result.Username);
        Assert.Equal("Alice Nguyen", result.DisplayName);
    }

    // Xac nhan UserDto khong co truong Email (bao ve thong tin ca nhan)
    [Fact]
    public async Task GetProfile_UserDtoDoesNotHaveEmailProperty()
    {
        await _sut.GetProfileAsync(ExistingUserId);
        var props = typeof(UserDto).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain("Email", props);
    }

    // Chua xac thuc (user khong ton tai) -> KeyNotFoundException -> HTTP 404
    [Fact]
    public async Task GetProfile_NonExistingUser_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.GetProfileAsync(MissingUserId));
    }

    // === UpdateProfileAsync ===

    [Fact]
    public async Task UpdateProfile_ValidDisplayName_ReturnsUpdatedDto()
    {
        var req = new UpdateProfileRequest { DisplayName = "Alice Updated" };
        var result = await _sut.UpdateProfileAsync(ExistingUserId, req);
        Assert.Equal("Alice Updated", result.DisplayName);
        Assert.Equal(ExistingUserId.ToString(), result.Id);
        Assert.Equal("alice", result.Username);
    }

    [Fact]
    public async Task UpdateProfile_TrimsLeadingTrailingWhitespace()
    {
        var req = new UpdateProfileRequest { DisplayName = "  Trimmed  " };
        var result = await _sut.UpdateProfileAsync(ExistingUserId, req);
        Assert.Equal("Trimmed", result.DisplayName);
    }

    // Validate: DisplayName chi chua khoang trang -> ArgumentException -> HTTP 400
    [Fact]
    public async Task UpdateProfile_WhitespaceDisplayName_ThrowsArgumentException()
    {
        var req = new UpdateProfileRequest { DisplayName = "   " };
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpdateProfileAsync(ExistingUserId, req));
    }

    // Validate: DisplayName > 100 ky tu -> ArgumentException -> HTTP 400
    [Fact]
    public async Task UpdateProfile_TooLongDisplayName_ThrowsArgumentException()
    {
        var req = new UpdateProfileRequest { DisplayName = new string('A', 101) };
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateProfileAsync(ExistingUserId, req));
        Assert.Contains("100", ex.Message);
    }

    // Validate: request null -> ArgumentException
    [Fact]
    public async Task UpdateProfile_NullRequest_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateProfileAsync(ExistingUserId, null!));
    }

    // Khong tim thay user -> KeyNotFoundException -> HTTP 404
    [Fact]
    public async Task UpdateProfile_NonExistingUser_ThrowsKeyNotFound()
    {
        var req = new UpdateProfileRequest { DisplayName = "Ghost" };
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UpdateProfileAsync(MissingUserId, req));
    }

    // Khong duoc cap nhat ho so nguoi khac: cap nhat alice khong anh huong bob
    [Fact]
    public async Task UpdateProfile_OnlyAffectsTargetUser_NotOthers()
    {
        var bobId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        await _sut.UpdateProfileAsync(ExistingUserId, new UpdateProfileRequest { DisplayName = "Alice Renamed" });
        var bob = await _sut.GetProfileAsync(bobId);
        Assert.Equal("Bob Tran", bob.DisplayName);
    }

    // === SearchAsync ===

    [Fact]
    public async Task Search_MatchingUsername_ReturnsResults()
    {
        var results = await _sut.SearchAsync("alice");
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Contains("alice", r.Username, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmptyList()
    {
        var results = await _sut.SearchAsync("nonexistentxyz");
        Assert.Empty(results);
    }

    // Ket qua tim kiem khong chua Email (API_CONTRACT: Khong tra Email trong ket qua cong khai)
    [Fact]
    public async Task Search_Results_DoNotHaveEmailProperty()
    {
        var results = await _sut.SearchAsync("alice");
        Assert.NotEmpty(results);
        var props = typeof(UserDto).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain("Email", props);
    }

    // Ket qua khong chua thong tin noi bo Identity (PasswordHash, SecurityStamp, v.v.)
    [Fact]
    public async Task Search_Results_DoNotHaveSensitiveIdentityFields()
    {
        var props = typeof(UserDto).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("PasswordHash", props);
        Assert.DoesNotContain("SecurityStamp", props);
        Assert.DoesNotContain("ConcurrencyStamp", props);
        Assert.DoesNotContain("NormalizedUserName", props);
        Assert.DoesNotContain("NormalizedEmail", props);
    }

    // Username rong -> ArgumentException -> HTTP 400 USERNAME_REQUIRED
    [Fact]
    public async Task Search_EmptyUsername_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SearchAsync(""));
    }

    // Username chi khoang trang -> ArgumentException
    [Fact]
    public async Task Search_WhitespaceUsername_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SearchAsync("   "));
    }

    // Username > 100 ky tu -> ArgumentException -> HTTP 400
    [Fact]
    public async Task Search_TooLongUsername_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.SearchAsync(new string('a', 101)));
    }

    // Tim kiem khong phan biet hoa/thuong
    [Fact]
    public async Task Search_CaseInsensitive_ReturnsSameCount()
    {
        var lower = await _sut.SearchAsync("alice");
        var upper = await _sut.SearchAsync("ALICE");
        Assert.Equal(lower.Count, upper.Count);
    }
}