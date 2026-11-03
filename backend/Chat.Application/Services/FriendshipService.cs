using Chat.Application.DTOs.Friendships;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;

namespace Chat.Application.Services;

/// <summary>
/// Friendship use cases. The authenticated user id is supplied by the API
/// boundary; it must never be accepted from a client-controlled body/query.
/// </summary>
public sealed class FriendshipService : IFriendshipService, IFriendshipQueryService
{
    private readonly IFriendshipRepository _repository;

    public FriendshipService(IFriendshipRepository repository)
    {
        _repository = repository;
    }

    public async Task<FriendshipDto> SendRequestAsync(Guid currentUserId, Guid targetUserId)
    {
        ValidateUserIds(currentUserId, targetUserId);
        if (currentUserId == targetUserId)
            throw new FriendshipException("SELF_FRIEND_REQUEST", "Bạn không thể tự gửi lời mời kết bạn.", 400);

        if (!await _repository.UserExistsAsync(targetUserId))
            throw new FriendshipException("USER_NOT_FOUND", "Không tìm thấy người dùng cần kết bạn.", 404);

        var (lowId, highId) = NormalizePair(currentUserId, targetUserId);
        var existing = await _repository.FindByPairAsync(lowId, highId);

        if (existing is not null)
        {
            if (existing.Status == "Pending")
                throw new FriendshipException("FRIEND_REQUEST_ALREADY_EXISTS", "Lời mời kết bạn đang chờ xử lý.", 409);

            if (existing.Status == "Accepted")
                throw new FriendshipException("ALREADY_FRIENDS", "Hai người đã là bạn bè.", 409);

            // Rejected/Cancelled are terminal states for the previous request.
            // Reuse the unique pair row and allow either side to initiate again.
            if (!await _repository.TryReopenAsync(existing, currentUserId))
                throw new FriendshipException("FRIEND_REQUEST_ALREADY_EXISTS", "Lời mời kết bạn vừa được tạo bởi một yêu cầu đồng thời.", 409);
        }
        else
        {
            var friendship = new Friendship
            {
                UserLowId = lowId,
                UserHighId = highId,
                RequesterUserId = currentUserId,
                Status = "Pending",
                CreatedAt = DateTimeOffset.UtcNow
            };

            // The DB composite PK is the final protection against two requests
            // arriving simultaneously for the same unordered pair.
            if (!await _repository.TryInsertAsync(friendship))
                throw new FriendshipException("FRIEND_REQUEST_ALREADY_EXISTS", "Lời mời kết bạn đang chờ xử lý.", 409);
        }

        return await _repository.GetUserSummaryAsync(targetUserId, "Pending")
               ?? throw new FriendshipException("USER_NOT_FOUND", "Không tìm thấy người dùng.", 404);
    }

    public Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId)
    {
        EnsureAuthenticatedId(currentUserId);
        return _repository.GetFriendsAsync(currentUserId);
    }

    public Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId)
    {
        EnsureAuthenticatedId(currentUserId);
        return _repository.GetRequestsAsync(currentUserId);
    }

    public async Task AcceptAsync(Guid currentUserId, Guid requesterUserId)
    {
        var friendship = await GetPendingRequestForRecipientAsync(currentUserId, requesterUserId);
        if (!await _repository.TryChangeStatusAsync(friendship, "Pending", "Accepted"))
            throw new FriendshipException("FRIEND_REQUEST_ALREADY_PROCESSED", "Lời mời đã được xử lý bởi một yêu cầu khác.", 409);
    }

    public async Task RejectAsync(Guid currentUserId, Guid requesterUserId)
    {
        var friendship = await GetPendingRequestForRecipientAsync(currentUserId, requesterUserId);
        if (!await _repository.TryChangeStatusAsync(friendship, "Pending", "Rejected"))
            throw new FriendshipException("FRIEND_REQUEST_ALREADY_PROCESSED", "Lời mời đã được xử lý bởi một yêu cầu khác.", 409);
    }

    public async Task RemoveAsync(Guid currentUserId, Guid targetUserId)
    {
        ValidateUserIds(currentUserId, targetUserId);
        if (currentUserId == targetUserId)
            throw FriendshipException.Validation("Bạn không thể hủy quan hệ với chính mình.");

        var (lowId, highId) = NormalizePair(currentUserId, targetUserId);
        var friendship = await _repository.FindByPairAsync(lowId, highId);

        if (friendship is null)
            throw new FriendshipException("FRIENDSHIP_NOT_FOUND", "Không tìm thấy quan hệ bạn bè hoặc lời mời.", 404);

        if (friendship.Status == "Pending")
        {
            if (friendship.RequesterUserId != currentUserId)
                throw new FriendshipException("ONLY_REQUESTER_CAN_CANCEL", "Chỉ người gửi mới được hủy lời mời kết bạn.", 403);
        }
        else if (friendship.Status != "Accepted")
        {
            throw new FriendshipException("FRIENDSHIP_NOT_ACTIVE", "Quan hệ này không còn ở trạng thái có thể hủy.", 409);
        }

        if (!await _repository.TryChangeStatusAsync(friendship, friendship.Status, "Cancelled"))
            throw new FriendshipException("FRIENDSHIP_ALREADY_CHANGED", "Quan hệ đã được thay đổi bởi một yêu cầu khác.", 409);
    }

    public async Task<bool> AreFriendsAsync(Guid userAId, Guid userBId)
    {
        if (userAId == Guid.Empty || userBId == Guid.Empty || userAId == userBId)
            return false;

        var (lowId, highId) = NormalizePair(userAId, userBId);
        var friendship = await _repository.FindByPairAsync(lowId, highId);
        return friendship?.Status == "Accepted";
    }

    private async Task<Friendship> GetPendingRequestForRecipientAsync(Guid currentUserId, Guid requesterUserId)
    {
        ValidateUserIds(currentUserId, requesterUserId);
        if (currentUserId == requesterUserId)
            throw FriendshipException.Validation("Người gửi và người nhận lời mời phải khác nhau.");

        var (lowId, highId) = NormalizePair(currentUserId, requesterUserId);
        var friendship = await _repository.FindByPairAsync(lowId, highId);

        if (friendship is null || friendship.Status != "Pending")
            throw new FriendshipException("FRIEND_REQUEST_NOT_FOUND", "Không tìm thấy lời mời kết bạn đang chờ.", 404);

        if (friendship.RequesterUserId == currentUserId)
            throw new FriendshipException("ONLY_RECIPIENT_CAN_RESPOND", "Chỉ người nhận lời mời mới được chấp nhận hoặc từ chối.", 403);

        if (friendship.RequesterUserId != requesterUserId)
            throw new FriendshipException("FRIEND_REQUEST_NOT_FOUND", "Không tìm thấy lời mời kết bạn đang chờ.", 404);

        return friendship;
    }

    private static (Guid Low, Guid High) NormalizePair(Guid userAId, Guid userBId) =>
        userAId.CompareTo(userBId) < 0 ? (userAId, userBId) : (userBId, userAId);

    private static void EnsureAuthenticatedId(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new FriendshipException("UNAUTHORIZED", "Bạn chưa đăng nhập.", 401);
    }

    private static void ValidateUserIds(Guid currentUserId, Guid targetUserId)
    {
        EnsureAuthenticatedId(currentUserId);
        if (targetUserId == Guid.Empty)
            throw FriendshipException.Validation("targetUserId không hợp lệ.");
    }
}
