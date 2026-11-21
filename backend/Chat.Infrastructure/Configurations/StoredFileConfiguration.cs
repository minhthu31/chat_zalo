namespace Chat.Infrastructure.Configurations;

using Chat.Domain.Entities;
using Chat.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("StoredFiles", table =>
        {
            table.HasCheckConstraint("CK_StoredFiles_SizePositive", "\"SizeBytes\" > 0");
            table.HasCheckConstraint("CK_StoredFiles_StorageKeyNonEmpty", "length(btrim(\"StorageKey\")) > 0");
            table.HasCheckConstraint("CK_StoredFiles_FileNameNonEmpty", "length(btrim(\"FileName\")) > 0");
            table.HasCheckConstraint("CK_StoredFiles_ContentTypeNonEmpty", "length(btrim(\"ContentType\")) > 0");
            table.HasCheckConstraint("CK_StoredFiles_UsageStatusAndSize", """
                ("UsageType" = 'Avatar' AND "ConversationId" IS NULL
                    AND "Status" IN ('Pending', 'Active', 'Inactive')
                    AND "SizeBytes" <= 2097152)
                OR
                ("UsageType" = 'Attachment' AND "ConversationId" IS NOT NULL
                    AND "Status" IN ('Pending', 'Attached')
                    AND "SizeBytes" <= 10485760)
                """.ReplaceLineEndings("\n"));
        });

        builder.HasKey(file => file.Id);

        builder.Property(file => file.UsageType).HasMaxLength(20).IsRequired();
        builder.Property(file => file.Status).HasMaxLength(20).IsRequired();
        builder.Property(file => file.StorageKey).HasMaxLength(512).IsRequired();
        builder.Property(file => file.FileName).HasMaxLength(255).IsRequired();
        builder.Property(file => file.ContentType).HasMaxLength(255).IsRequired();
        builder.Property(file => file.SizeBytes).IsRequired();
        builder.Property(file => file.CreatedAt).IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(file => file.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(file => file.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(file => file.StorageKey).IsUnique();
        builder.HasIndex(file => file.UploadedByUserId);
        builder.HasIndex(file => file.ConversationId);
        builder.HasIndex(file => new { file.Status, file.CreatedAt });
    }
}
