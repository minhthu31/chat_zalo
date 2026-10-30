namespace Chat.Infrastructure.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Chat.Domain.Entities;
using Chat.Infrastructure.Identity;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations", table =>
        {
            table.HasCheckConstraint("CK_Conversations_Kind", "\"Kind\" IN ('Direct', 'Group')");
            table.HasCheckConstraint("CK_Conversations_LastMessageSeq", "\"LastMessageSeq\" >= 0");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Kind)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.LastMessageSeq)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.LastActivityAt)
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.CreatedByUserId);

        builder.HasOne(c => c.DirectConversation)
            .WithOne(d => d.Conversation)
            .HasForeignKey<DirectConversation>(d => d.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Members)
            .WithOne(m => m.Conversation)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Messages)
            .WithOne(m => m.Conversation)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
