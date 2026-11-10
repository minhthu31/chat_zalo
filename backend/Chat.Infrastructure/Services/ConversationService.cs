using Chat.Application.DTOs.Conversations;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Chat.Infrastructure.Services;

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

    private static (Guid low, Guid high) NormaliseUserPair(
        Guid first,
        Guid second)
    {
        return string.Compare(
            first.ToString("D"),
            second.ToString("D"),
            StringComparison.OrdinalIgnoreCase) < 0
                ? (first, second)
                : (second, first);
    }

    private static ConversationDto ToDto(
        Conversation conversation,
        bool isNewlyCreated = false)
    {
        return new ConversationDto
        {
            Id = conversation.Id,
            Kind = conversation.Kind,
            LastMessageSeq = conversation.LastMessageSeq,
            IsNewlyCreated = isNewlyCreated
        };
    }

    public async Task<ConversationDto> CreateDirectAsync(
        Guid currentUserId,
        Guid targetUserId)
    {
        if (currentUserId == Guid.Empty || targetUserId == Guid.Empty)
            throw new ArgumentException("User IDs must not be empty.");

        if (currentUserId == targetUserId)
        {
            throw new UnauthorizedAccessException(
                "DIRECT_CONVERSATION_NOT_ALLOWED");
        }

        var targetExists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == targetUserId);

        if (!targetExists)
            throw new KeyNotFoundException("NOT_FOUND");

        var (userLowId, userHighId) =
            NormaliseUserPair(currentUserId, targetUserId);

        var existing = await _dbContext.DirectConversations
            .FirstOrDefaultAsync(direct =>
                direct.UserLowId == userLowId &&
                direct.UserHighId == userHighId);

        if (existing is not null)
        {
            return await GetExistingAsync(
                existing.ConversationId,
                currentUserId,
                targetUserId);
        }

        if (await _blockService.IsBlockedAsync(
                currentUserId,
                targetUserId))
        {
            throw new UnauthorizedAccessException(
                "DIRECT_CONVERSATION_NOT_ALLOWED");
        }

        if (!await _friendshipService.AreFriendsAsync(
                currentUserId,
                targetUserId))
        {
            throw new UnauthorizedAccessException(
                "DIRECT_CONVERSATION_NOT_ALLOWED");
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync();

        try
        {
            var now = DateTimeOffset.UtcNow;

            var conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                CreatedByUserId = currentUserId,
                Kind = "Direct",
                LastMessageSeq = 0L,
                CreatedAt = now,
                LastActivityAt = now
            };

            var directConversation = new DirectConversation
            {
                ConversationId = conversation.Id,
                UserLowId = userLowId,
                UserHighId = userHighId
            };

            var currentMember = new ConversationMember
            {
                ConversationId = conversation.Id,
                UserId = currentUserId,
                Role = "Member",
                JoinedSeq = 0L,
                JoinedAt = now,
                LastReadSeq = 0L,
                LastDeliveredSeq = 0L
            };

            var targetMember = new ConversationMember
            {
                ConversationId = conversation.Id,
                UserId = targetUserId,
                Role = "Member",
                JoinedSeq = 0L,
                JoinedAt = now,
                LastReadSeq = 0L,
                LastDeliveredSeq = 0L
            };

            _dbContext.Conversations.Add(conversation);
            _dbContext.DirectConversations.Add(directConversation);
            _dbContext.ConversationMembers.Add(currentMember);
            _dbContext.ConversationMembers.Add(targetMember);

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return ToDto(conversation, isNewlyCreated: true);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_DirectConversations_UserLowId_UserHighId"
            })
        {
            // Một request khác đã tạo hội thoại cho cùng cặp người dùng.
            await transaction.RollbackAsync();
            await transaction.DisposeAsync();

            _dbContext.ChangeTracker.Clear();

            var winner = await _dbContext.DirectConversations
                .FirstOrDefaultAsync(direct =>
                    direct.UserLowId == userLowId &&
                    direct.UserHighId == userHighId)
                ?? throw new InvalidOperationException(
                    "Race condition resolved but existing record could not be found.");

            // Vẫn phải kiểm tra quyền trước khi trả hội thoại của request thắng.
            return await GetExistingAsync(
                winner.ConversationId,
                currentUserId,
                targetUserId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<ConversationDto> GetExistingAsync(
        Guid conversationId,
        Guid currentUserId,
        Guid targetUserId)
    {
        var isActiveMember = await _dbContext.ConversationMembers
            .AnyAsync(member =>
                member.ConversationId == conversationId &&
                member.UserId == currentUserId &&
                member.LeftAt == null);

        // Người chặn giữ quyền xem lịch sử.
        // Người bị đối phương chặn không được truy cập.
        if (!isActiveMember ||
            await _blockService.HasBlockedAsync(
                targetUserId,
                currentUserId))
        {
            throw new UnauthorizedAccessException(
                "CONVERSATION_ACCESS_DENIED");
        }

        var conversation = await _dbContext.Conversations
            .AsNoTracking()
            .SingleAsync(item => item.Id == conversationId);

        return ToDto(conversation);
    }

    public async Task<List<ConversationDto>> GetConversationsAsync(
        Guid currentUserId)
    {
        var items = await _dbContext.ConversationMembers
            .AsNoTracking()
            .Where(member =>
                member.UserId == currentUserId &&
                member.LeftAt == null)
            .Join(
                _dbContext.Conversations,
                member => member.ConversationId,
                conversation => conversation.Id,
                (member, conversation) => new
                {
                    Member = member,
                    Conversation = conversation
                })
            .Where(item =>
                !_dbContext.DirectConversations.Any(direct =>
                    direct.ConversationId == item.Conversation.Id &&
                    _dbContext.UserBlocks.Any(block =>
                        block.BlockedUserId == currentUserId &&
                        (block.BlockerUserId == direct.UserLowId ||
                         block.BlockerUserId == direct.UserHighId))))
            .OrderByDescending(item =>
                item.Conversation.LastActivityAt)
            .ToListAsync();

        return items
            .Select(item => ToDto(item.Conversation))
            .ToList();
    }
}
