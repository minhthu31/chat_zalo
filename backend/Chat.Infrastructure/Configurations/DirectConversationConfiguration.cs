namespace Chat.Infrastructure.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Chat.Domain.Entities;
using Chat.Infrastructure.Identity;

public class DirectConversationConfiguration : IEntityTypeConfiguration<DirectConversation>
{
    public void Configure(EntityTypeBuilder<DirectConversation> builder)
    {
        builder.ToTable("DirectConversations", table =>
        {
            table.HasCheckConstraint("CK_DirectConversations_UserOrder", "\"UserLowId\" < \"UserHighId\"");
        });

        builder.HasKey(d => d.ConversationId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(d => d.UserLowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(d => d.UserHighId)
            .OnDelete(DeleteBehavior.Restrict);

        // Ràng buộc mỗi cặp người dùng chỉ có một hội thoại trực tiếp duy nhất
        builder.HasIndex(d => new { d.UserLowId, d.UserHighId })
            .IsUnique();

        builder.HasIndex(d => d.UserHighId);
    }
}
