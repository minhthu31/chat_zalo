namespace Chat.Infrastructure.Configurations;

using Chat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class GroupAdminTransferRequestConfiguration : IEntityTypeConfiguration<GroupAdminTransferRequest>
{
    public void Configure(EntityTypeBuilder<GroupAdminTransferRequest> builder)
    {
        builder.ToTable("GroupAdminTransferRequests", table =>
        {
            table.HasCheckConstraint("CK_GroupAdminTransferRequests_DifferentUsers", "\"FromUserId\" <> \"ToUserId\"");
            table.HasCheckConstraint("CK_GroupAdminTransferRequests_Status", "\"Status\" IN ('Pending', 'Accepted', 'Rejected', 'Cancelled')");
            table.HasCheckConstraint("CK_GroupAdminTransferRequests_Resolution", "(\"Status\" = 'Pending') = (\"ResolvedAt\" IS NULL)");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();

        builder.HasOne(r => r.GroupConversation)
            .WithMany(g => g.AdminTransferRequests)
            .HasForeignKey(r => r.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ConversationMember>()
            .WithMany()
            .HasForeignKey(r => new { r.ConversationId, r.FromUserId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ConversationMember>()
            .WithMany()
            .HasForeignKey(r => new { r.ConversationId, r.ToUserId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.ConversationId, r.FromUserId });
        builder.HasIndex(r => new { r.ConversationId, r.ToUserId });
        builder.HasIndex(r => r.ConversationId)
            .IsUnique()
            .HasDatabaseName("IX_GroupAdminTransferRequests_OnePending")
            .HasFilter("\"Status\" = 'Pending'");
    }
}
