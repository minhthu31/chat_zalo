namespace Chat.Infrastructure.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Chat.Domain.Entities;
using Chat.Infrastructure.Identity;

public class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("Friendships", table =>
        {
            table.HasCheckConstraint("CK_Friendships_UserOrder", "\"UserLowId\" < \"UserHighId\"");
            table.HasCheckConstraint("CK_Friendships_RequesterUserId", "(\"RequesterUserId\" = \"UserLowId\") OR (\"RequesterUserId\" = \"UserHighId\")");
            table.HasCheckConstraint("CK_Friendships_Status", "\"Status\" IN ('Pending', 'Accepted', 'Rejected', 'Cancelled')");
        });

        builder.HasKey(f => new { f.UserLowId, f.UserHighId });

        builder.Property(f => f.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(f => f.CreatedAt)
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(f => f.UserLowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(f => f.UserHighId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(f => f.RequesterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(f => f.RequesterUserId);
        builder.HasIndex(f => f.UserHighId);
    }
}
