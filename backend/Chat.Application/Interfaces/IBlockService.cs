namespace Chat.Application.Interfaces;

public interface IBlockService
{
    Task BlockUserAsync(Guid blockerUserId, Guid blockedUserId);

    Task UnblockUserAsync(Guid blockerUserId, Guid blockedUserId);

    Task<bool> IsBlockedAsync(Guid userId1, Guid userId2);

    Task<bool> IsBlockedByUserAsync(Guid blockerUserId, Guid blockedUserId);
}