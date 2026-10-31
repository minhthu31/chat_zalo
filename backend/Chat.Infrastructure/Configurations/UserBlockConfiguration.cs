namespace Chat.Infrastructure.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Chat.Domain.Entities;
using Chat.Infrastructure.Identity;

public class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> builder)
    {
        builder.ToTable("UserBlocks", table =>
        {
            table.HasCheckConstraint("CK_UserBlocks_SelfBlock", "\"BlockerUserId\" <> \"BlockedUserId\"");
        });

        builder.HasKey(b => new { b.BlockerUserId, b.BlockedUserId });

        builder.Property(b => b.CreatedAt)
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(b => b.BlockerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(b => b.BlockedUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => b.BlockedUserId);
    }
}
