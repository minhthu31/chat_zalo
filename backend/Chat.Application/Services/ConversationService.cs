using Chat.Application.DTOs.Conversations;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Chat.Application.Services;

public class ConversationService : IConversationService
{
    private readonly ChatDbContext _dbContext;
    private readonly IFriendshipService _friendshipService;
    private readonly IBlockService _blockService;

    public ConversationService(
        ChatDbContext dbContext,
        IFriendshipService friendshipService,
        IBlockService blockService)
    {
        _dbContext = dbContext;
        _friendshipService = friendshipService;
        _blockService = blockService;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Normalises two user IDs so that userLowId &lt; userHighId using ordinal
    /// "D"-format GUID string comparison — matches the DB constraint
    /// CK_DirectConversations_UserOrder ("UserLowId" &lt; "UserHighId").
    /// </summary>
    private static (Guid low, Guid high) NormaliseUserPair(Guid u1, Guid u2)
        => string.Compare(u1.ToString("D"), u2.ToString("D"),
               StringComparison.OrdinalIgnoreCase) < 0
           ? (u1, u2)
           : (u2, u1);

    private static ConversationDto ToDto(
        Conversation conv,
        bool isNewlyCreated = false)
        => new()
        {
            Id             = conv.Id,
            Kind           = conv.Kind,
            LastMessageSeq = conv.LastMessageSeq,
            IsNewlyCreated = isNewlyCreated,
        };

    // -----------------------------------------------------------------------
    // CreateDirectAsync
    // -----------------------------------------------------------------------

    public async Task<ConversationDto> CreateDirectAsync(Guid currentUserId, Guid targetUserId)
    {
        // Basic guard: both IDs must be valid
        if (currentUserId == Guid.Empty || targetUserId == Guid.Empty)
            throw new ArgumentException("User IDs must not be empty.");

        // Cannot chat with yourself
        if (currentUserId == targetUserId)
            throw new UnauthorizedAccessException("DIRECT_CONVERSATION_NOT_ALLOWED");

        var targetExists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == targetUserId);
        if (!targetExists)
        {
            throw new KeyNotFoundException("NOT_FOUND");
        }

        var (userLowId, userHighId) = NormaliseUserPair(currentUserId, targetUserId);

        // ---- Idempotent path: conversation already exists ----
        var existing = await _dbContext.DirectConversations
            .FirstOrDefaultAsync(dc => dc.UserLowId == userLowId && dc.UserHighId == userHighId);

        if (existing is not null)
        {
            return await GetExistingAsync(existing.ConversationId, currentUserId, targetUserId);
        }

        // ---- New conversation path ----

        // Block check (bidirectional: either party blocked the other)
        if (await _blockService.IsBlockedAsync(currentUserId, targetUserId))
            throw new UnauthorizedAccessException("DIRECT_CONVERSATION_NOT_ALLOWED");

        // Friendship check
        if (!await _friendshipService.AreFriendsAsync(currentUserId, targetUserId))
            throw new UnauthorizedAccessException("DIRECT_CONVERSATION_NOT_ALLOWED");

        // ---- Create inside a transaction ----
        await using var tx = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;

            var conversation = new Conversation
            {
                Id              = Guid.NewGuid(),
                CreatedByUserId = currentUserId,
                Kind            = "Direct",
                LastMessageSeq  = 0L,
                CreatedAt       = now,
                LastActivityAt  = now,
            };

            var directConv = new DirectConversation
            {
                ConversationId = conversation.Id,
                UserLowId      = userLowId,
                UserHighId     = userHighId,
            };

            var memberCurrent = new ConversationMember
            {
                ConversationId   = conversation.Id,
                UserId           = currentUserId,
                Role             = "Member",
                JoinedSeq        = 0L,
                JoinedAt         = now,
                LastReadSeq      = 0L,
                LastDeliveredSeq = 0L,
            };

            var memberTarget = new ConversationMember
            {
                ConversationId   = conversation.Id,
                UserId           = targetUserId,
                Role             = "Member",
                JoinedSeq        = 0L,
                JoinedAt         = now,
                LastReadSeq      = 0L,
                LastDeliveredSeq = 0L,
            };

            _dbContext.Conversations.Add(conversation);
            _dbContext.DirectConversations.Add(directConv);
            _dbContext.ConversationMembers.Add(memberCurrent);
            _dbContext.ConversationMembers.Add(memberTarget);

            await _dbContext.SaveChangesAsync();
            await tx.CommitAsync();

            return ToDto(conversation, isNewlyCreated: true);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_DirectConversations_UserLowId_UserHighId"
            })
        {
            // Race condition: another concurrent request already created the conversation.
            await tx.RollbackAsync();
            await tx.DisposeAsync();
            _dbContext.ChangeTracker.Clear();

            var race = await _dbContext.DirectConversations
                .FirstOrDefaultAsync(dc => dc.UserLowId == userLowId && dc.UserHighId == userHighId)
                ?? throw new InvalidOperationException(
                    "Race condition resolved but existing record could not be found.");

            return await GetExistingAsync(race.ConversationId, currentUserId, targetUserId);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<ConversationDto> GetExistingAsync(Guid conversationId, Guid currentUserId, Guid targetUserId)
    {
        // The creator of a block retains history; its recipient does not.
        // Apply this equally to normal retrieval and the concurrent-create fallback.
        if (!await _dbContext.ConversationMembers.AnyAsync(m =>
                m.ConversationId == conversationId && m.UserId == currentUserId && m.LeftAt == null)
            || await _blockService.HasBlockedAsync(targetUserId, currentUserId))
            throw new UnauthorizedAccessException("CONVERSATION_ACCESS_DENIED");

        var conversation = await _dbContext.Conversations.AsNoTracking()
            .SingleAsync(c => c.Id == conversationId);
        return ToDto(conversation);
    }

    // -----------------------------------------------------------------------
    // GetConversationsAsync
    // -----------------------------------------------------------------------

    public async Task<List<ConversationDto>> GetConversationsAsync(Guid currentUserId)
    {
        // Only conversations where currentUser is an active member (LeftAt == null)
        var items = await _dbContext.ConversationMembers
            .AsNoTracking()
            .Where(m => m.UserId == currentUserId && m.LeftAt == null)
            .Join(_dbContext.Conversations,
                m => m.ConversationId,
                c => c.Id,
                (m, c) => new { Member = m, Conversation = c })
            .Where(x => !_dbContext.DirectConversations.Any(d =>
                d.ConversationId == x.Conversation.Id
                && _dbContext.UserBlocks.Any(b => b.BlockedUserId == currentUserId
                    && (b.BlockerUserId == d.UserLowId || b.BlockerUserId == d.UserHighId))))
            .OrderByDescending(x => x.Conversation.LastActivityAt)
            .ToListAsync();

        return items
            .Select(x => ToDto(x.Conversation))
            .ToList();
    }
}
