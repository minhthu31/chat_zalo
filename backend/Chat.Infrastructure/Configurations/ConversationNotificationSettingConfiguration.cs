namespace Chat.Infrastructure.Configurations;

using Chat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ConversationNotificationSettingConfiguration : IEntityTypeConfiguration<ConversationNotificationSetting>
{
    public void Configure(EntityTypeBuilder<ConversationNotificationSetting> builder)
    {
        builder.ToTable("ConversationNotificationSettings");
        builder.HasKey(setting => new { setting.ConversationId, setting.UserId });
        builder.Property(setting => setting.IsMuted).HasDefaultValue(false).IsRequired();

        builder.HasOne<ConversationMember>()
            .WithMany()
            .HasForeignKey(setting => new { setting.ConversationId, setting.UserId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(setting => setting.UserId);
    }
}
