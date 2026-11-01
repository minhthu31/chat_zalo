namespace Chat.Application.Interfaces;

public interface IBlockService
{
    /// <summary>
    /// Returns true if either userA has blocked userB OR userB has blocked userA
    /// (bidirectional check).
    /// </summary>
    Task<bool> IsBlockedAsync(Guid userAId, Guid userBId, CancellationToken ct = default);

    /// <summary>
    /// Returns true only if <paramref name="blockerUserId"/> has explicitly blocked
    /// <paramref name="blockedUserId"/> (one-directional check).
    /// </summary>
    Task<bool> HasBlockedAsync(Guid blockerUserId, Guid blockedUserId, CancellationToken ct = default);
}

