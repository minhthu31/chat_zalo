using System.Security.Claims;
using Chat.Api.Filters;
using Chat.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Chat.Api.Controllers;

[ApiController]
[Authorize]
[ValidateAntiforgeryJson]
[Route("api/presence")]
public sealed class PresenceController(ChatDbContext db, IMemoryCache cache) : ControllerBase
{
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(22);

    [HttpPost("heartbeat")]
    public async Task<ActionResult<FriendPresence[]>> Heartbeat(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var now = DateTimeOffset.UtcNow;
        cache.Set((typeof(PresenceController), userId), now, TimeSpan.FromDays(30));

        var friends = await db.Friendships.AsNoTracking()
            .Where(f => f.Status == "Accepted" && (f.UserLowId == userId || f.UserHighId == userId))
            .Select(f => f.UserLowId == userId ? f.UserHighId : f.UserLowId)
            .Where(friend => !db.UserBlocks.Any(b =>
                (b.BlockerUserId == userId && b.BlockedUserId == friend) ||
                (b.BlockerUserId == friend && b.BlockedUserId == userId)))
            .ToArrayAsync(cancellationToken);

        return friends.Select(friend =>
        {
            var lastSeen = cache.Get<DateTimeOffset?>((typeof(PresenceController), friend));
            return new FriendPresence(friend, now - lastSeen < OnlineWindow, lastSeen);
        }).ToArray();
    }

    public sealed record FriendPresence(Guid UserId, bool IsOnline, DateTimeOffset? LastSeenAt);
}
