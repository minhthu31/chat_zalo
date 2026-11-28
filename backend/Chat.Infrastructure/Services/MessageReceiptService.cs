using System.Data;
using Chat.Application.DTOs.Receipts;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.Services;

/// <summary>
/// Stores one shared receipt cursor per account and conversation.
/// A shared conversation lock coordinates with message sequence allocation, while a
/// member-row lock serializes updates from multiple devices for the same account.
/// </summary>
public sealed class MessageReceiptService : IMessageReceiptService
{
    private readonly ChatDbContext _db;
    private readonly IGroupPermissionService _groupPermissions;
    private readonly IReceiptEventPublisher _eventPublisher;
    private readonly ILogger<MessageReceiptService> _logger;

    public MessageReceiptService(
        ChatDbContext db,
        IGroupPermissionService groupPermissions,
        IReceiptEventPublisher eventPublisher,
        ILogger<MessageReceiptService> logger)
    {
        _db = db;
        _groupPermissions = groupPermissions;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<ConversationReceiptsDto> GetAsync(
        Guid currentUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? messageIds = null)
    {
        ValidateIds(currentUserId, conversationId);

        if (messageIds is { Count: > IMessageReceiptService.MaxMessageIds })
            throw new ReceiptServiceException(400, "VALIDATION_ERROR", "Too many message IDs.");

        var conversation = await _db.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken)
            ?? throw NotFound();

        await EnsureCanViewAsync(currentUserId, conversation, cancellationToken);

        var members = _db.ConversationMembers
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.LeftAt == null);

        // In groups, a user's messages and receipt activity are hidden from
        // a member whom that user has blocked. Direct-chat block semantics are
        // intentionally left identical to MessageService.GetHistoryAsync.
        if (conversation.Kind == "Group")
        {
            members = members.Where(m => !_db.UserBlocks.Any(b =>
                (b.BlockerUserId == m.UserId && b.BlockedUserId == currentUserId) ||
                (b.BlockerUserId == currentUserId && b.BlockedUserId == m.UserId)));
        }

        var rows = await (
            from member in members
            join user in _db.Users.AsNoTracking() on member.UserId equals user.Id
            orderby user.DisplayName, user.UserName
            select new
            {
                member.UserId,
                user.DisplayName,
                user.UserName,
                member.LastDeliveredSeq,
                member.LastReadSeq
            })
            .ToListAsync(cancellationToken);

        var messageReceipts = new List<MessageReceiptItemDto>();
        if (conversation.Kind == "Group")
        {
            var joinedSeq = await members.Where(m => m.UserId == currentUserId)
                .Select(m => (long?)m.JoinedSeq)
                .SingleOrDefaultAsync(cancellationToken) ?? throw AccessDenied();
            var visibleMessages = _db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId && m.Seq >= joinedSeq &&
                    !_db.UserBlocks.Any(b =>
                        (b.BlockerUserId == m.SenderUserId && b.BlockedUserId == currentUserId) ||
                        (b.BlockerUserId == currentUserId && b.BlockedUserId == m.SenderUserId)));
            if (messageIds is not null)
                visibleMessages = visibleMessages.Where(m => messageIds.Contains(m.Id));

            var selected = await visibleMessages.OrderByDescending(m => m.Seq)
                .Take(IMessageReceiptService.MaxMessageIds)
                .Select(m => new { m.Id, m.Seq, m.SenderUserId })
                .ToListAsync(cancellationToken);
            var selectedIds = selected.Select(m => m.Id).ToArray();
            var visibleUserIds = rows.Select(r => r.UserId).ToArray();
            var recorded = await _db.MessageUserReceipts.AsNoTracking()
                .Where(r => selectedIds.Contains(r.MessageId) && visibleUserIds.Contains(r.UserId))
                .Select(r => new { r.MessageId, r.UserId, r.DeliveredAt, r.ReadAt })
                .ToListAsync(cancellationToken);
            var receiptsByMessage = recorded.ToLookup(r => r.MessageId);
            selected.Reverse();
            messageReceipts = selected.Select(m => new MessageReceiptItemDto
            {
                MessageId = m.Id,
                Seq = m.Seq,
                SenderUserId = m.SenderUserId,
                DeliveredByUserIds = receiptsByMessage[m.Id]
                    .Where(r => r.DeliveredAt != null && r.UserId != m.SenderUserId)
                    .Select(r => r.UserId).ToList(),
                ReadByUserIds = receiptsByMessage[m.Id]
                    .Where(r => r.ReadAt != null && r.UserId != m.SenderUserId)
                    .Select(r => r.UserId).ToList()
            }).ToList();
        }

        return new ConversationReceiptsDto
        {
            ConversationId = conversationId,
            Messages = messageReceipts,
            Items = rows.Select(row => new ConversationReceiptItemDto
            {
                UserId = row.UserId,
                DisplayName = string.IsNullOrWhiteSpace(row.DisplayName)
                    ? (row.UserName ?? string.Empty)
                    : row.DisplayName,
                DeliveredSeq = row.LastDeliveredSeq,
                ReadSeq = row.LastReadSeq
            }).ToList()
        };
    }

    public Task<ReceiptDto> MarkDeliveredAsync(
        Guid currentUserId,
        Guid conversationId,
        UpdateReceiptRequest request,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(currentUserId, conversationId, request, markRead: false, cancellationToken: cancellationToken);

    public Task<ReceiptDto> MarkReadAsync(
        Guid currentUserId,
        Guid conversationId,
        UpdateReceiptRequest request,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(currentUserId, conversationId, request, markRead: true, cancellationToken: cancellationToken);

    private async Task<ReceiptDto> UpdateAsync(
        Guid currentUserId,
        Guid conversationId,
        UpdateReceiptRequest request,
        bool markRead,
        CancellationToken cancellationToken)
    {
        ValidateIds(currentUserId, conversationId);

        if (request is null || request.Seq < 0)
            throw InvalidSequence();

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        // MessageService takes FOR UPDATE on this same row before allocating
        // LastMessageSeq. FOR SHARE blocks an in-flight send while allowing
        // different members to update their own receipt rows concurrently.
        var conversations = await _db.Conversations
            .FromSqlInterpolated($"""
                SELECT *
                FROM "Conversations"
                WHERE "Id" = {conversationId}
                FOR SHARE
                """)
            .ToListAsync(cancellationToken);

        var conversation = conversations.SingleOrDefault()
            ?? throw NotFound();

        // A per-member FOR UPDATE lock serializes acknowledgements from the
        // user's multiple devices and makes the max-cursor update race-safe.
        var memberRows = await _db.ConversationMembers
            .FromSqlInterpolated($"""
                SELECT *
                FROM "ConversationMembers"
                WHERE "ConversationId" = {conversationId}
                  AND "UserId" = {currentUserId}
                  AND "LeftAt" IS NULL
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        var member = memberRows.SingleOrDefault()
            ?? throw AccessDenied();

        // Raw SQL locking queries may return an already tracked entity without
        // refreshing its values. Reload both locked rows so this context uses
        // the latest committed cursors while the transaction still holds locks.
        await _db.Entry(conversation).ReloadAsync(cancellationToken);
        await _db.Entry(member).ReloadAsync(cancellationToken);

        await EnsureCanViewAsync(currentUserId, conversation, cancellationToken);

        if (conversation.Kind == "Group" && request.Seq > 0)
        {
            var groupResult = await UpdateGroupMessageAsync(currentUserId, conversationId,
                request.Seq, markRead, member, conversation, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (groupResult.Changed)
            {
                try
                {
                    await _eventPublisher.PublishAsync(new MessageReceiptChangedDto
                    {
                        ConversationId = conversationId, UserId = currentUserId,
                        MessageId = groupResult.Receipt.MessageId,
                        Seq = groupResult.Receipt.Seq,
                        IsDelivered = groupResult.Receipt.IsDelivered,
                        IsRead = groupResult.Receipt.IsRead,
                        DeliveredSeq = groupResult.Receipt.DeliveredSeq,
                        ReadSeq = groupResult.Receipt.ReadSeq
                    }, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish message receipt {ConversationId}/{Seq}", conversationId, request.Seq);
                }
            }
            return groupResult.Receipt;
        }

        var currentCursor = markRead ? member.LastReadSeq : member.LastDeliveredSeq;
        if (request.Seq <= currentCursor)
        {
            var current = new ReceiptDto
            {
                UserId = currentUserId,
                DeliveredSeq = member.LastDeliveredSeq,
                ReadSeq = member.LastReadSeq
            };

            await transaction.CommitAsync(cancellationToken);
            return current;
        }

        if (request.Seq > conversation.LastMessageSeq ||
            request.Seq < Math.Max(1L, member.JoinedSeq))
        {
            throw InvalidSequence();
        }

        var targetExists = await _db.Messages.AnyAsync(m =>
            m.ConversationId == conversationId && m.Seq == request.Seq, cancellationToken);
        if (!targetExists)
            throw InvalidSequence();

        var oldDelivered = member.LastDeliveredSeq;
        var oldRead = member.LastReadSeq;

        member.LastDeliveredSeq = Math.Max(member.LastDeliveredSeq, request.Seq);
        if (markRead)
            member.LastReadSeq = Math.Max(member.LastReadSeq, request.Seq);

        // Reading a message necessarily means it has been delivered.
        if (member.LastReadSeq > member.LastDeliveredSeq)
            member.LastDeliveredSeq = member.LastReadSeq;

        if (member.LastReadSeq > member.LastDeliveredSeq ||
            member.LastDeliveredSeq > conversation.LastMessageSeq)
        {
            throw InvalidSequence();
        }

        var changed = oldDelivered != member.LastDeliveredSeq ||
                      oldRead != member.LastReadSeq;

        if (changed)
            await _db.SaveChangesAsync(cancellationToken);

        var result = new ReceiptDto
        {
            UserId = currentUserId,
            DeliveredSeq = member.LastDeliveredSeq,
            ReadSeq = member.LastReadSeq
        };

        await transaction.CommitAsync(cancellationToken);

        // Never publish before the receipt cursor is committed. A notification
        // failure must not undo a successful receipt update.
        if (changed)
        {
            try
            {
                await _eventPublisher.PublishAsync(
                    new MessageReceiptChangedDto
                    {
                        ConversationId = conversationId,
                        UserId = currentUserId,
                        DeliveredSeq = result.DeliveredSeq,
                        ReadSeq = result.ReadSeq
                    },
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to publish receipt change for conversation {ConversationId} and user {UserId}",
                    conversationId,
                    currentUserId);
            }
        }

        return result;
    }

    private async Task<(ReceiptDto Receipt, bool Changed)> UpdateGroupMessageAsync(
        Guid userId, Guid conversationId, long seq, bool markRead,
        ConversationMember member, Conversation conversation, CancellationToken cancellationToken)
    {
        if (seq > conversation.LastMessageSeq || seq < Math.Max(1L, member.JoinedSeq))
            throw InvalidSequence();
        var message = await _db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.Seq == seq)
            .Select(m => new { m.Id, m.SenderUserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (message is null)
            throw InvalidSequence();
        if (await _db.UserBlocks.AnyAsync(b =>
            (b.BlockerUserId == message.SenderUserId && b.BlockedUserId == userId) ||
            (b.BlockerUserId == userId && b.BlockedUserId == message.SenderUserId), cancellationToken))
            throw AccessDenied();

        // The conversation/member locks held by UpdateAsync serialize concurrent retries.
        var receipt = await _db.MessageUserReceipts
            .SingleOrDefaultAsync(r => r.MessageId == message.Id && r.UserId == userId, cancellationToken);
        var changed = false;
        if (receipt is null)
        {
            receipt = new MessageUserReceipt { MessageId = message.Id, UserId = userId };
            _db.MessageUserReceipts.Add(receipt);
        }
        var now = DateTimeOffset.UtcNow;
        if (receipt.DeliveredAt is null)
        {
            receipt.DeliveredAt = now;
            changed = true;
        }
        if (markRead && receipt.ReadAt is null)
        {
            receipt.ReadAt = now;
            changed = true;
        }
        if (changed)
            await _db.SaveChangesAsync(cancellationToken);

        return (new ReceiptDto
        {
            UserId = userId, MessageId = message.Id, Seq = seq,
            IsDelivered = receipt.DeliveredAt != null, IsRead = receipt.ReadAt != null,
            DeliveredSeq = member.LastDeliveredSeq, ReadSeq = member.LastReadSeq
        }, changed);
    }

    private async Task EnsureCanViewAsync(
        Guid currentUserId,
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        if (conversation.Kind == "Group")
        {
            var permission = await _groupPermissions.CanViewMessagesAsync(
                conversation.Id,
                currentUserId,
                cancellationToken);

            if (!permission.Allowed)
            {
                throw new ReceiptServiceException(
                    permission.HttpStatus ?? 403,
                    permission.ErrorCode ?? "GROUP_ACCESS_DENIED",
                    permission.Message ?? "Bạn không có quyền truy cập nhóm này.");
            }

            return;
        }

        if (conversation.Kind != "Direct")
            throw AccessDenied();

        var direct = await _db.DirectConversations
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.ConversationId == conversation.Id, cancellationToken);

        if (direct is null ||
            (direct.UserLowId != currentUserId && direct.UserHighId != currentUserId))
        {
            throw AccessDenied();
        }

        var peerId = direct.UserLowId == currentUserId
            ? direct.UserHighId
            : direct.UserLowId;

        var activeMember = await _db.ConversationMembers.AnyAsync(m =>
            m.ConversationId == conversation.Id &&
            m.UserId == currentUserId &&
            m.LeftAt == null,
            cancellationToken);

        if (!activeMember)
            throw AccessDenied();

        // Match direct-message visibility: if the peer blocked the caller,
        // the caller cannot read the conversation. A caller blocking the peer
        // does not hide the existing history.
        var blockedByPeer = await _db.UserBlocks.AnyAsync(b =>
            b.BlockerUserId == peerId &&
            b.BlockedUserId == currentUserId,
            cancellationToken);

        if (blockedByPeer)
            throw AccessDenied();
    }

    private static void ValidateIds(Guid currentUserId, Guid conversationId)
    {
        if (currentUserId == Guid.Empty)
            throw new ReceiptServiceException(401, "UNAUTHORIZED", "Bạn chưa đăng nhập.");

        if (conversationId == Guid.Empty)
            throw new ReceiptServiceException(400, "VALIDATION_ERROR", "conversationId phải khác rỗng.");
    }

    private static ReceiptServiceException InvalidSequence() =>
        new(400, "INVALID_RECEIPT_SEQ", "Seq phải là tin nhắn tồn tại, người dùng được phép xem và không vượt quá LastMessageSeq.");

    private static ReceiptServiceException AccessDenied() =>
        new(403, "CONVERSATION_ACCESS_DENIED", "Bạn không có quyền xem hoặc xác nhận tin nhắn trong hội thoại này.");

    private static ReceiptServiceException NotFound() =>
        new(404, "NOT_FOUND", "Không tìm thấy hội thoại.");
}
