namespace Chat.Infrastructure.Configurations;

using Chat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class GroupConversationConfiguration : IEntityTypeConfiguration<GroupConversation>
{
    public void Configure(EntityTypeBuilder<GroupConversation> builder)
    {
        builder.ToTable("GroupConversations", table =>
            table.HasCheckConstraint("CK_GroupConversations_Name", "\"Name\" ~ '[^[:space:]]'"));

        builder.HasKey(g => g.ConversationId);
        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(g => g.DeletedAt);
    }
}
