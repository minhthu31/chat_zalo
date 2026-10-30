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
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // 1. Cấu hình bảng Users & ASP.NET Core Identity
        builder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("Users");
            b.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();

            b.HasIndex(u => u.NormalizedUserName).HasDatabaseName("UserNameIndex").IsUnique();
            b.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
        });

        builder.Entity<IdentityRole<Guid>>(b => b.ToTable("Roles"));
        builder.Entity<IdentityUserRole<Guid>>(b => b.ToTable("UserRoles"));
        builder.Entity<IdentityUserClaim<Guid>>(b => b.ToTable("UserClaims"));
        builder.Entity<IdentityUserLogin<Guid>>(b => b.ToTable("UserLogins"));
        builder.Entity<IdentityRoleClaim<Guid>>(b => b.ToTable("RoleClaims"));
        builder.Entity<IdentityUserToken<Guid>>(b => b.ToTable("UserTokens"));

        // 2. Bảng Friendships
        builder.Entity<Friendship>(b =>
        {
            b.ToTable("Friendships");
            b.HasKey(f => new { f.UserLowId, f.UserHighId });
            b.Property(f => f.Status).HasMaxLength(20).IsRequired();

            b.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(f => f.UserLowId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(f => f.UserHighId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(f => f.RequesterUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 3. Bảng UserBlocks
        builder.Entity<UserBlock>(b =>
        {
            b.ToTable("UserBlocks");
            b.HasKey(ub => new { ub.BlockerUserId, ub.BlockedUserId });

            b.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(ub => ub.BlockerUserId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(ub => ub.BlockedUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. Bảng Conversations
        builder.Entity<Conversation>(b =>
        {
            b.ToTable("Conversations");
            b.HasKey(c => c.Id);
            b.Property(c => c.Kind).HasMaxLength(20).IsRequired();
            b.Property(c => c.LastMessageSeq).HasDefaultValue(0L);

            b.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(c => c.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 5. Bảng DirectConversations
        builder.Entity<DirectConversation>(b =>
        {
            b.ToTable("DirectConversations");
            b.HasKey(dc => dc.ConversationId);

            b.HasIndex(dc => new { dc.UserLowId, dc.UserHighId }).IsUnique();

            // Chỉ định rõ ràng quan hệ 1-1 với khóa chính ConversationId đồng thời là khóa ngoại
            b.HasOne(dc => dc.Conversation)
                .WithOne(c => c.DirectConversation)
                .HasForeignKey<DirectConversation>(dc => dc.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(dc => dc.UserLowId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(dc => dc.UserHighId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 6. Bảng ConversationMembers
        builder.Entity<ConversationMember>(b =>
        {
            b.ToTable("ConversationMembers");
            b.HasKey(cm => new { cm.ConversationId, cm.UserId });
            b.Property(cm => cm.Role).HasMaxLength(20).IsRequired();

            b.HasOne(cm => cm.Conversation)
                .WithMany(c => c.Members)
                .HasForeignKey(cm => cm.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(cm => cm.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 7. Bảng Messages
        builder.Entity<Message>(b =>
        {
            b.ToTable("Messages");
            b.HasKey(m => m.Id);
            b.Property(m => m.Body).HasMaxLength(4000).IsRequired();
            b.Property(m => m.ClientMessageId).IsRequired();

            b.HasIndex(m => new { m.ConversationId, m.Seq }).IsUnique();
            b.HasIndex(m => new { m.ConversationId, m.SenderUserId, m.ClientMessageId }).IsUnique();

            b.HasOne(m => m.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(m => m.SenderUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Friendship>().ToTable("Friendships", t =>
        {
            t.HasCheckConstraint("CK_Friendships_UserOrder", "\"UserLowId\" < \"UserHighId\"");
            t.HasCheckConstraint("CK_Friendships_RequesterUserId", "(\"RequesterUserId\" = \"UserLowId\") OR (\"RequesterUserId\" = \"UserHighId\")");
            t.HasCheckConstraint("CK_Friendships_Status", "\"Status\" IN ('Pending', 'Accepted', 'Rejected', 'Cancelled')");
        });
        builder.Entity<UserBlock>().ToTable("UserBlocks", t =>
            t.HasCheckConstraint("CK_UserBlocks_SelfBlock", "\"BlockerUserId\" <> \"BlockedUserId\""));
        builder.Entity<Conversation>().ToTable("Conversations", t =>
        {
            t.HasCheckConstraint("CK_Conversations_Kind", "\"Kind\" IN ('Direct', 'Group')");
            t.HasCheckConstraint("CK_Conversations_LastMessageSeq", "\"LastMessageSeq\" >= 0");
        });
        builder.Entity<DirectConversation>().ToTable("DirectConversations", t =>
            t.HasCheckConstraint("CK_DirectConversations_UserOrder", "\"UserLowId\" < \"UserHighId\""));
        builder.Entity<ConversationMember>(b =>
        {
            b.Property(m => m.Role).HasDefaultValue("Member");
            b.Property(m => m.JoinedSeq).HasDefaultValue(0L);
            b.Property(m => m.LastReadSeq).HasDefaultValue(0L);
            b.Property(m => m.LastDeliveredSeq).HasDefaultValue(0L);
            b.ToTable("ConversationMembers", t =>
            {
                t.HasCheckConstraint("CK_ConversationMembers_Role", "\"Role\" IN ('Member', 'Admin', 'Owner')");
                t.HasCheckConstraint("CK_ConversationMembers_LastReadSeqNonNegative", "\"LastReadSeq\" >= 0");
                t.HasCheckConstraint("CK_ConversationMembers_LastDeliveredSeqNonNegative", "\"LastDeliveredSeq\" >= 0");
                t.HasCheckConstraint("CK_ConversationMembers_SeqReadDelivered", "\"LastReadSeq\" <= \"LastDeliveredSeq\"");
            });
        });
    }
}
