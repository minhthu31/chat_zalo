namespace Chat.Infrastructure.Configurations;

using Chat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachment>
{
    public void Configure(EntityTypeBuilder<MessageAttachment> builder)
    {
        builder.ToTable("MessageAttachments");
        builder.HasKey(link => new { link.MessageId, link.StoredFileId });

        builder.HasOne(link => link.Message)
            .WithMany()
            .HasForeignKey(link => link.MessageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(link => link.StoredFile)
            .WithMany()
            .HasForeignKey(link => link.StoredFileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(link => link.StoredFileId).IsUnique();
    }
}
