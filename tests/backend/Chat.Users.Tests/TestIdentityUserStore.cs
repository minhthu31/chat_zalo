using System.Collections.Concurrent;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Chat.Users.Tests;

/// <summary>
/// Minimal Identity store for API tests that exercise the real cookie/Security Stamp
/// validator without requiring PostgreSQL. It is test infrastructure only; application
/// services and module business logic remain unchanged.
/// </summary>
internal sealed class TestIdentityUserStore : IUserStore<ApplicationUser>, IUserSecurityStampStore<ApplicationUser>
{
    private readonly ConcurrentDictionary<string, ApplicationUser> _users = new(StringComparer.Ordinal);

    public TestIdentityUserStore() { }

    public ApplicationUser EnsureUser(Guid id)
    {
        return _users.GetOrAdd(id.ToString(), _ =>
        {
            var name = $"test_{id:N}";
            return new ApplicationUser
            {
                Id = id,
                UserName = name,
                NormalizedUserName = name.ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString("N"),
                DisplayName = "Authentication test user"
            };
        });
    }

    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_users.TryAdd(user.Id.ToString(), user)
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError { Code = "DuplicateUserId" }));
    }

    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _users[user.Id.ToString()] = user;
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _users.TryRemove(user.Id.ToString(), out _);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _users.TryGetValue(userId, out var user);
        return Task.FromResult(user);
    }

    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _users.Values.FirstOrDefault(x => x.NormalizedUserName == normalizedUserName);
        return Task.FromResult(user);
    }

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(user.Id.ToString());
    }

    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(user.UserName);
    }

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(user.NormalizedUserName);
    }

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(user.SecurityStamp);
    }

    public void Dispose() { }
}
