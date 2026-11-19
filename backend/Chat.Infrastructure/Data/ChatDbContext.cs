using Chat.Domain.Entities;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Data;

public class ChatDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options)
    {
    }

    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<DirectConversation> DirectConversations => Set<DirectConversation>();
    public DbSet<ConversationMember> ConversationMembers => Set<ConversationMember>();
    public DbSet<GroupConversation> GroupConversations => Set<GroupConversation>();
    public DbSet<GroupAdminTransferRequest> GroupAdminTransferRequests => Set<GroupAdminTransferRequest>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<ConversationNotificationSetting> ConversationNotificationSettings => Set<ConversationNotificationSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly);
    }
}
