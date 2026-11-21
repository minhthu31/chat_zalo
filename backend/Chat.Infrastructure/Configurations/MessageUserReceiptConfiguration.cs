using Chat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Configurations;

public sealed class MessageUserReceiptConfiguration : IEntityTypeConfiguration<MessageUserReceipt>
{
    public void Configure(EntityTypeBuilder<MessageUserReceipt> builder)
    {
        builder.ToTable("MessageUserReceipts", t =>
        {
            t.HasCheckConstraint("CK_MessageUserReceipts_ReadRequiresDelivered", "\"ReadAt\" IS NULL OR \"DeliveredAt\" IS NOT NULL");
        });
        builder.HasKey(x => new { x.MessageId, x.UserId });
        builder.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Chat.Infrastructure.Identity.ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.UserId);
    }
}
