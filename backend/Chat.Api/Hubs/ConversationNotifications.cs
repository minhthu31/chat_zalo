using Chat.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Chat.Api.Hubs;

public sealed record ConversationChanged(Guid EventId, Guid ConversationId, long LatestSeq);

// Server-side integration point for the message HTTP handler AFTER commit.
// No public Hub method can invoke this publisher.
public sealed class ConversationNotifications(ChatDbContext db, IHubContext<ChatHub> hub)
{
    public async Task PublishAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        // Refuse publication before the caller's explicit transaction commits.
        if (db.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Publish conversation notifications only after commit.");

        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => new { c.Id, c.LastMessageSeq })
            .SingleOrDefaultAsync(cancellationToken);
        if (conversation is null || conversation.LastMessageSeq <= 0) return;

        // Re-evaluate permission on each publication, including connections
        // opened before a block or membership change. Never cache recipient groups.
        var recipients = await db.ConversationMembers.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.LeftAt == null)
            .Where(m => !db.UserBlocks.Any(b => b.BlockedUserId == m.UserId
                && db.ConversationMembers.Any(peer => peer.ConversationId == conversationId
                    && peer.UserId == b.BlockerUserId && peer.LeftAt == null)))
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);
        if (recipients.Count == 0) return;

        await hub.Clients.Users(recipients.Select(id => id.ToString("D")).ToArray())
            .SendAsync("ConversationChanged",
                new ConversationChanged(Guid.NewGuid(), conversation.Id, conversation.LastMessageSeq),
                cancellationToken);
    }
}
