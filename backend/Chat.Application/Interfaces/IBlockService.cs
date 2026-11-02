
namespace Chat.Application.Interfaces;

public interface IBlockService
{
    Task BlockAsync(
        Guid currentUserId,
        Guid targetUserId);

    Task UnblockAsync(
        Guid currentUserId,
        Guid targetUserId);

    Task<bool> IsBlockedAsync(
        Guid userAId,
        Guid userBId);

    Task<bool> HasBlockedAsync(
        Guid blockerUserId,
        Guid blockedUserId);
}

