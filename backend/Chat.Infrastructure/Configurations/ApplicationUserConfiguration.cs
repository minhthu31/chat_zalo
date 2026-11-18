namespace Chat.Infrastructure.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Chat.Domain.Entities;
using Chat.Infrastructure.Identity;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("Users");

        builder.Property(u => u.DisplayName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(u => u.Bio)
            .HasMaxLength(200)
            .HasDefaultValue(string.Empty)
            .IsRequired();

        builder.HasOne<StoredFile>()
            .WithMany()
            .HasForeignKey(u => u.AvatarFileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => u.AvatarFileId).IsUnique();

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        builder.HasIndex(u => u.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("UserNameIndex");

        builder.HasIndex(u => u.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("EmailIndex");
    }
}
