namespace Chat.Application.Interfaces;

public interface IFriendshipService
{
    /// <summary>Returns true if userA and userB have an Accepted friendship.</summary>
    Task<bool> AreFriendsAsync(Guid userAId, Guid userBId, CancellationToken ct = default);
}

