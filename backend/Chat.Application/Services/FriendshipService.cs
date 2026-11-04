using Chat.Application.DTOs.Friendships;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Chat.Application.Services;

/// <summary>Implements the friendship lifecycle against the canonical, unordered user pair.</summary>
public sealed class FriendshipService(ChatDbContext db) : IFriendshipService
{
    public async Task<FriendshipDto> SendRequestAsync(Guid currentUserId, Guid targetUserId)
    {
        ValidateDistinctUsers(currentUserId, targetUserId);
        await RequireUserAsync(currentUserId, "CURRENT_USER_NOT_FOUND");
        await RequireUserAsync(targetUserId, "USER_NOT_FOUND");

        var (low, high) = Normalize(currentUserId, targetUserId);
        var relationship = await db.Friendships.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserLowId == low && x.UserHighId == high);

        if (relationship is not null)
        {
            if (relationship.Status == "Pending")
                throw Error(409, "FRIEND_REQUEST_ALREADY_EXISTS", "A pending friend request already exists for this user pair.");
            if (relationship.Status == "Accepted")
                throw Error(409, "ALREADY_FRIENDS", "These users are already friends.");

            // Conditional UPDATE makes re-opening a terminal pair atomic: two concurrent
            // senders cannot both turn the same Rejected/Cancelled row into Pending.
            var now = DateTimeOffset.UtcNow;
            var changed = await db.Friendships
                .Where(x => x.UserLowId == low && x.UserHighId == high &&
                            (x.Status == "Rejected" || x.Status == "Cancelled"))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.RequesterUserId, currentUserId)
                    .SetProperty(x => x.Status, "Pending")
                    .SetProperty(x => x.CreatedAt, now)
                    .SetProperty(x => x.UpdatedAt, (DateTimeOffset?)null));
            db.Entry(relationship).State = EntityState.Detached;
            if (changed == 0)
            {
                var latestStatus = await db.Friendships.AsNoTracking()
                    .Where(x => x.UserLowId == low && x.UserHighId == high)
                    .Select(x => x.Status).SingleAsync();
                if (latestStatus == "Pending")
                    throw Error(409, "FRIEND_REQUEST_ALREADY_EXISTS", "A pending friend request already exists for this user pair.");
                if (latestStatus == "Accepted")
                    throw Error(409, "ALREADY_FRIENDS", "These users are already friends.");
                throw Error(409, "FRIENDSHIP_STATE_CONFLICT", "The friendship changed while the request was being sent.");
            }
        }
        else
        {
            relationship = new Friendship
            {
                UserLowId = low,
                UserHighId = high,
                RequesterUserId = currentUserId,
                Status = "Pending",
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Friendships.Add(relationship);
            try
            {
                await db.SaveChangesAsync();
                db.Entry(relationship).State = EntityState.Detached;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // A concurrent request won the composite-PK race. Translate it to the API contract.
                db.Entry(relationship).State = EntityState.Detached;
                throw Error(409, "FRIEND_REQUEST_ALREADY_EXISTS", "A pending friend request already exists for this user pair.");
            }
        }

        return await BuildDtoAsync(targetUserId, "Pending");
    }

    public Task AcceptAsync(Guid currentUserId, Guid requesterUserId) =>
        TransitionIncomingAsync(currentUserId, requesterUserId, "Accepted");

    public Task RejectAsync(Guid currentUserId, Guid requesterUserId) =>
        TransitionIncomingAsync(currentUserId, requesterUserId, "Rejected");

    private async Task TransitionIncomingAsync(Guid currentUserId, Guid requesterUserId, string nextStatus)
    {
        ValidateDistinctUsers(currentUserId, requesterUserId);
        var (low, high) = Normalize(currentUserId, requesterUserId);

        // Lock the canonical pair before reading it. Under PostgreSQL READ COMMITTED,
        // SELECT ... FOR UPDATE waits for a concurrent writer and then returns the
        // committed row version. This serializes accept/reject against cancel/re-send
        // without adding a column or changing the API contract.
        await using var transaction = await db.Database.BeginTransactionAsync();
        var relationship = await LoadForUpdateAsync(low, high)
            ?? throw Error(404, "FRIENDSHIP_NOT_FOUND", "Friend request or friendship was not found.");
        EnsurePendingIncoming(relationship, currentUserId, requesterUserId);

        var changed = await db.Friendships
            .Where(x => x.UserLowId == low && x.UserHighId == high &&
                        x.Status == "Pending" && x.RequesterUserId == requesterUserId &&
                        x.CreatedAt == relationship.CreatedAt)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, nextStatus)
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow));
        if (changed != 1)
            throw Error(409, "FRIEND_REQUEST_STATE_CONFLICT", "The friend request changed while it was being processed.");

        await transaction.CommitAsync();
    }

    public async Task RemoveAsync(Guid currentUserId, Guid targetUserId)
    {
        ValidateDistinctUsers(currentUserId, targetUserId);
        var (low, high) = Normalize(currentUserId, targetUserId);

        // Use the same row lock as Accept/Reject so a remove cannot act on a stale
        // snapshot while another transaction cancels and re-opens this pair.
        await using var transaction = await db.Database.BeginTransactionAsync();
        var relationship = await LoadForUpdateAsync(low, high)
            ?? throw Error(404, "FRIENDSHIP_NOT_FOUND", "No active friend request or friendship exists.");

        if (relationship.Status == "Pending" && relationship.RequesterUserId != currentUserId)
            throw Error(403, "FRIEND_REQUEST_SENDER_ONLY", "Only the sender can cancel an outgoing friend request.");
        if (relationship.Status != "Pending" && relationship.Status != "Accepted")
            throw Error(404, "FRIENDSHIP_NOT_FOUND", "No active friend request or friendship exists.");

        var expectedStatus = relationship.Status;
        var changed = await db.Friendships
            .Where(x => x.UserLowId == low && x.UserHighId == high &&
                        x.Status == expectedStatus &&
                        x.CreatedAt == relationship.CreatedAt &&
                        (expectedStatus != "Pending" || x.RequesterUserId == currentUserId))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, "Cancelled")
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow));
        if (changed != 1)
            throw Error(409, "FRIENDSHIP_STATE_CONFLICT", "The friendship changed while it was being removed.");

        await transaction.CommitAsync();
    }

    private Task<Friendship?> LoadForUpdateAsync(Guid low, Guid high) =>
        db.Friendships
            .FromSqlInterpolated($"""
                SELECT * FROM "Friendships"
                WHERE "UserLowId" = {low} AND "UserHighId" = {high}
                FOR UPDATE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync();

    public Task<bool> AreFriendsAsync(Guid userAId, Guid userBId)
    {
        if (userAId == Guid.Empty || userBId == Guid.Empty || userAId == userBId)
            return Task.FromResult(false);

        var (low, high) = Normalize(userAId, userBId);
        return db.Friendships.AsNoTracking().AnyAsync(x =>
            x.UserLowId == low && x.UserHighId == high && x.Status == "Accepted");
    }

    public async Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId)
    {
        var rows = await db.Friendships.AsNoTracking()
            .Where(x => x.Status == "Accepted" &&
                        (x.UserLowId == currentUserId || x.UserHighId == currentUserId))
            .Join(db.Users.AsNoTracking(),
                friendship => friendship.UserLowId == currentUserId
                    ? friendship.UserHighId : friendship.UserLowId,
                user => user.Id,
                (friendship, user) => new FriendshipDto
                {
                    UserId = user.Id,
                    Username = user.UserName ?? string.Empty,
                    DisplayName = user.DisplayName,
                    Status = friendship.Status
                })
            .OrderBy(x => x.DisplayName)
            .ToListAsync();

        return rows;
    }

    public async Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId)
    {
        var pending = await db.Friendships.AsNoTracking()
            .Where(x => x.Status == "Pending" &&
                        (x.UserLowId == currentUserId || x.UserHighId == currentUserId))
            .ToListAsync();

        var otherUserIds = pending.Select(x => x.RequesterUserId == currentUserId
            ? (x.UserLowId == currentUserId ? x.UserHighId : x.UserLowId)
            : x.RequesterUserId).Distinct().ToArray();

        var users = await db.Users.AsNoTracking()
            .Where(x => otherUserIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        var result = new FriendshipRequestsDto();
        foreach (var item in pending)
        {
            var otherUserId = item.RequesterUserId == currentUserId
                ? (item.UserLowId == currentUserId ? item.UserHighId : item.UserLowId)
                : item.RequesterUserId;
            if (!users.TryGetValue(otherUserId, out var user))
                continue;

            var dto = new FriendshipDto
            {
                UserId = user.Id,
                Username = user.UserName ?? string.Empty,
                DisplayName = user.DisplayName,
                Status = "Pending"
            };
            if (item.RequesterUserId == currentUserId)
                result.Outgoing.Add(dto);
            else
                result.Incoming.Add(dto);
        }

        result.Incoming = result.Incoming.OrderBy(x => x.DisplayName).ToList();
        result.Outgoing = result.Outgoing.OrderBy(x => x.DisplayName).ToList();
        return result;
    }

    private static void EnsurePendingIncoming(Friendship relationship, Guid currentUserId, Guid requesterUserId)
    {
        if (relationship.Status != "Pending")
            throw Error(409, "FRIEND_REQUEST_NOT_PENDING", "The friend request is no longer pending.");
        if (relationship.RequesterUserId != requesterUserId || relationship.RequesterUserId == currentUserId)
            throw Error(403, "FRIEND_REQUEST_RECIPIENT_ONLY", "Only the recipient can accept or reject this friend request.");
    }

    private async Task RequireUserAsync(Guid userId, string code)
    {
        if (!await db.Users.AnyAsync(x => x.Id == userId))
            throw Error(404, code, "User does not exist.");
    }

    private static void ValidateDistinctUsers(Guid currentUserId, Guid targetUserId)
    {
        if (currentUserId == Guid.Empty)
            throw Error(401, "UNAUTHORIZED", "Authentication is required.");
        if (targetUserId == Guid.Empty)
            throw Error(400, "INVALID_TARGET_USER_ID", "Target user ID must be a valid non-empty GUID.");
        if (currentUserId == targetUserId)
            throw Error(400, "SELF_FRIEND_REQUEST_NOT_ALLOWED", "You cannot create a friendship with yourself.");
    }

    private async Task<FriendshipDto> BuildDtoAsync(Guid otherUserId, string status)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == otherUserId);
        return new FriendshipDto
        {
            UserId = user.Id,
            Username = user.UserName ?? string.Empty,
            DisplayName = user.DisplayName,
            Status = status
        };
    }

    private static (Guid Low, Guid High) Normalize(Guid a, Guid b) =>
        a.CompareTo(b) < 0 ? (a, b) : (b, a);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static FriendshipException Error(int statusCode, string code, string message) =>
        new(statusCode, code, message);
}

public sealed class FriendshipException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
