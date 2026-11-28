using Chat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Configurations;

public sealed class PinnedMessageConfiguration : IEntityTypeConfiguration<PinnedMessage>
{
    public void Configure(EntityTypeBuilder<PinnedMessage> builder)
    {
        builder.ToTable("PinnedMessages");
        builder.HasKey(pin => new { pin.ConversationId, pin.MessageId });

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(pin => new { pin.ConversationId, pin.MessageId })
            .HasPrincipalKey(message => new { message.ConversationId, message.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
