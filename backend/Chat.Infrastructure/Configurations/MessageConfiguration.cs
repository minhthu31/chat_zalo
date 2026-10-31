namespace Chat.Infrastructure.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Chat.Domain.Entities;
using Chat.Infrastructure.Identity;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Body)
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(m => m.ClientMessageId)
            .IsRequired();

        builder.Property(m => m.Seq)
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.SenderUserId);

        // Ràng buộc Seq duy nhất theo từng hội thoại
        builder.HasIndex(m => new { m.ConversationId, m.Seq })
            .IsUnique();

        // Ràng buộc chống trùng tin nhắn khi retry ClientMessageId theo từng sender trong hội thoại
        builder.HasIndex(m => new { m.ConversationId, m.SenderUserId, m.ClientMessageId })
            .IsUnique();
    }
}
