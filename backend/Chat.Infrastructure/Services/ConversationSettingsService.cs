using Chat.Application.DTOs.Notifications;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Chat.Infrastructure.Services;

public sealed class ConversationSettingsService : IConversationSettingsService
{
    private readonly ChatDbContext _db;

    public ConversationSettingsService(ChatDbContext db)
    {
        _db = db;
    }

    public async Task<NotificationSettingsDto> GetAsync(
        Guid currentUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveMemberAsync(currentUserId, conversationId, cancellationToken);

        var isMuted = await _db.ConversationNotificationSettings
            .AsNoTracking()
            .Where(s => s.ConversationId == conversationId && s.UserId == currentUserId)
            .Select(s => (bool?)s.IsMuted)
            .SingleOrDefaultAsync(cancellationToken) ?? false;

        return new NotificationSettingsDto
        {
            IsMuted = isMuted
        };
    }

    public async Task<NotificationSettingsDto> UpdateAsync(
        Guid currentUserId,
        Guid conversationId,
        UpdateNotificationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ConversationSettingsServiceException(
                400,
                "VALIDATION_ERROR",
                "Dữ liệu cài đặt thông báo không hợp lệ.");
        }

        await EnsureActiveMemberAsync(currentUserId, conversationId, cancellationToken);

        var setting = await _db.ConversationNotificationSettings
            .FirstOrDefaultAsync(
                s => s.ConversationId == conversationId && s.UserId == currentUserId,
                cancellationToken);

        if (setting is null)
        {
            setting = new ConversationNotificationSetting
            {
                ConversationId = conversationId,
                UserId = currentUserId,
                IsMuted = request.IsMuted
            };

            _db.ConversationNotificationSettings.Add(setting);
        }
        else
        {
            setting.IsMuted = request.IsMuted;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation or "23505"
            })
        {
            _db.Entry(setting).State = EntityState.Detached;

            var existing = await _db.ConversationNotificationSettings
                .FirstOrDefaultAsync(
                    s => s.ConversationId == conversationId && s.UserId == currentUserId,
                    cancellationToken);

            if (existing is not null)
            {
                existing.IsMuted = request.IsMuted;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        return new NotificationSettingsDto
        {
            IsMuted = request.IsMuted
        };
    }

    private async Task EnsureActiveMemberAsync(
        Guid currentUserId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        if (currentUserId == Guid.Empty)
        {
            throw new ConversationSettingsServiceException(
                401,
                "UNAUTHORIZED",
                "Bạn chưa đăng nhập.");
        }

        if (conversationId == Guid.Empty)
        {
            throw new ConversationSettingsServiceException(
                400,
                "VALIDATION_ERROR",
                "conversationId phải khác rỗng.");
        }

        var isActiveMember = await _db.ConversationMembers
            .AsNoTracking()
            .AnyAsync(
                m => m.ConversationId == conversationId &&
                     m.UserId == currentUserId &&
                     m.LeftAt == null,
                cancellationToken);

        if (!isActiveMember)
        {
            var kind = await _db.Conversations
                .AsNoTracking()
                .Where(c => c.Id == conversationId)
                .Select(c => c.Kind)
                .FirstOrDefaultAsync(cancellationToken);

            if (kind == "Group")
            {
                throw new ConversationSettingsServiceException(
                    403,
                    "GROUP_ACCESS_DENIED",
                    "Bạn không có quyền truy cập nhóm này.");
            }

            throw new ConversationSettingsServiceException(
                403,
                "CONVERSATION_ACCESS_DENIED",
                "Bạn không có quyền truy cập hội thoại này.");
        }

        var isDisbandedGroup = await _db.GroupConversations
            .AsNoTracking()
            .AnyAsync(
                g => g.ConversationId == conversationId && g.DeletedAt != null,
                cancellationToken);

        if (isDisbandedGroup)
        {
            throw new ConversationSettingsServiceException(
                403,
                "GROUP_ACCESS_DENIED",
                "Nhóm đã được giải tán.");
        }

        var isBlockedInDirect = await _db.DirectConversations
            .AsNoTracking()
            .Where(d =>
                d.ConversationId == conversationId &&
                (d.UserLowId == currentUserId || d.UserHighId == currentUserId))
            .AnyAsync(
                d => _db.UserBlocks.Any(b =>
                    b.BlockedUserId == currentUserId &&
                    b.BlockerUserId == (d.UserLowId == currentUserId ? d.UserHighId : d.UserLowId)),
                cancellationToken);

        if (isBlockedInDirect)
        {
            throw new ConversationSettingsServiceException(
                403,
                "CONVERSATION_ACCESS_DENIED",
                "Bạn không có quyền truy cập hội thoại này.");
        }
    }
}

