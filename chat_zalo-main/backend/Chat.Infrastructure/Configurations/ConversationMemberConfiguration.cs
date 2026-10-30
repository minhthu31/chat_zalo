namespace Chat.Infrastructure.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Chat.Domain.Entities;
using Chat.Infrastructure.Identity;

public class ConversationMemberConfiguration : IEntityTypeConfiguration<ConversationMember>
{
    public void Configure(EntityTypeBuilder<ConversationMember> builder)
    {
        builder.ToTable("ConversationMembers", table =>
        {
            table.HasCheckConstraint("CK_ConversationMembers_Role", "\"Role\" IN ('Member', 'Admin', 'Owner')");
            table.HasCheckConstraint("CK_ConversationMembers_LastReadSeqNonNegative", "\"LastReadSeq\" >= 0");
            table.HasCheckConstraint("CK_ConversationMembers_LastDeliveredSeqNonNegative", "\"LastDeliveredSeq\" >= 0");
            table.HasCheckConstraint("CK_ConversationMembers_SeqReadDelivered", "\"LastReadSeq\" <= \"LastDeliveredSeq\"");
        });

        builder.HasKey(m => new { m.ConversationId, m.UserId });

        builder.Property(m => m.Role)
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue("Member");

        builder.Property(m => m.JoinedSeq)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(m => m.JoinedAt)
            .IsRequired();

        builder.Property(m => m.LastReadSeq)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(m => m.LastDeliveredSeq)
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.UserId);
    }
}
