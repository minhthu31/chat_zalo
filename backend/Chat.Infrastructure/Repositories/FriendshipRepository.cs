using Chat.Application.DTOs.Friendships;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Chat.Infrastructure.Repositories;

/// <summary>EF Core/PostgreSQL persistence adapter for friendship use cases.</summary>
public sealed class FriendshipRepository : IFriendshipRepository
{
    private readonly ChatDbContext _db;

    public FriendshipRepository(ChatDbContext db)
    {
        _db = db;
    }

    public Task<bool> UserExistsAsync(Guid userId) =>
        _db.Users.AsNoTracking().AnyAsync(user => user.Id == userId);

    public Task<Friendship?> FindByPairAsync(Guid userLowId, Guid userHighId) =>
        _db.Friendships.SingleOrDefaultAsync(friendship =>
            friendship.UserLowId == userLowId && friendship.UserHighId == userHighId);

    public async Task<FriendshipDto?> GetUserSummaryAsync(Guid userId, string status)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => new { item.Id, item.UserName, item.DisplayName })
            .SingleOrDefaultAsync();

        if (user is null)
            return null;

        return new FriendshipDto
        {
            UserId = user.Id,
            Username = user.UserName ?? string.Empty,
            DisplayName = user.DisplayName,
            Status = status
        };
    }

    public async Task<List<FriendshipDto>> GetFriendsAsync(Guid currentUserId)
    {
        var rows = await (
            from friendship in _db.Friendships.AsNoTracking()
            where friendship.Status == "Accepted"
                  && (friendship.UserLowId == currentUserId || friendship.UserHighId == currentUserId)
            join user in _db.Users.AsNoTracking()
                on (friendship.UserLowId == currentUserId ? friendship.UserHighId : friendship.UserLowId)
                equals user.Id
            orderby user.UserName
            select new FriendshipDto
            {
                UserId = user.Id,
                Username = user.UserName ?? string.Empty,
                DisplayName = user.DisplayName,
                Status = friendship.Status
            }).ToListAsync();

        return rows;
    }

    public async Task<FriendshipRequestsDto> GetRequestsAsync(Guid currentUserId)
    {
        var incoming = await (
            from friendship in _db.Friendships.AsNoTracking()
            where friendship.Status == "Pending"
                  && friendship.RequesterUserId != currentUserId
                  && (friendship.UserLowId == currentUserId || friendship.UserHighId == currentUserId)
            join requester in _db.Users.AsNoTracking()
                on friendship.RequesterUserId equals requester.Id
            orderby requester.UserName
            select new FriendshipDto
            {
                UserId = requester.Id,
                Username = requester.UserName ?? string.Empty,
                DisplayName = requester.DisplayName,
                Status = friendship.Status
            }).ToListAsync();

        var outgoing = await (
            from friendship in _db.Friendships.AsNoTracking()
            where friendship.Status == "Pending"
                  && friendship.RequesterUserId == currentUserId
            join target in _db.Users.AsNoTracking()
                on (friendship.UserLowId == currentUserId ? friendship.UserHighId : friendship.UserLowId)
                equals target.Id
            orderby target.UserName
            select new FriendshipDto
            {
                UserId = target.Id,
                Username = target.UserName ?? string.Empty,
                DisplayName = target.DisplayName,
                Status = friendship.Status
            }).ToListAsync();

        return new FriendshipRequestsDto
        {
            Incoming = incoming,
            Outgoing = outgoing
        };
    }

    public async Task<bool> TryInsertAsync(Friendship friendship)
    {
        _db.Friendships.Add(friendship);
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgres
                  && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Detach the failed Added entity so this scoped DbContext can still
            // be used for the follow-up query/error mapping.
            _db.Entry(friendship).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryReopenAsync(Friendship friendship, Guid requesterUserId)
    {
        var now = DateTimeOffset.UtcNow;
        var affected = await _db.Friendships
            .Where(item => item.UserLowId == friendship.UserLowId
                           && item.UserHighId == friendship.UserHighId
                           && item.Status == friendship.Status
                           && (item.Status == "Rejected" || item.Status == "Cancelled"))
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.RequesterUserId, requesterUserId)
                .SetProperty(item => item.Status, "Pending")
                .SetProperty(item => item.CreatedAt, now)
                .SetProperty(item => item.UpdatedAt, (DateTimeOffset?)null));

        _db.Entry(friendship).State = EntityState.Detached;
        return affected == 1;
    }

    public async Task<bool> TryChangeStatusAsync(Friendship friendship, string expectedStatus, string newStatus)
    {
        var affected = await _db.Friendships
            .Where(item => item.UserLowId == friendship.UserLowId
                           && item.UserHighId == friendship.UserHighId
                           && item.Status == expectedStatus)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.Status, newStatus)
                .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow));

        _db.Entry(friendship).State = EntityState.Detached;
        return affected == 1;
    }
}
