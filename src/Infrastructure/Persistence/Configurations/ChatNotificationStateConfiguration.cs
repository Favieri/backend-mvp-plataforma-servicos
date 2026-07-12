using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ChatNotificationStateConfiguration : IEntityTypeConfiguration<ChatNotificationState>
{
    public void Configure(EntityTypeBuilder<ChatNotificationState> builder)
    {
        builder.ToTable("chat_notification_state");

        builder.HasKey(x => new { x.ConversationId, x.RecipientUserId });

        builder.Property(x => x.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(x => x.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
        builder.Property(x => x.LastNotifiedMessageId).HasColumnName("last_notified_message_id").IsRequired();
        builder.Property(x => x.NotifiedAt).HasColumnName("notified_at").IsRequired();

        builder.HasIndex(x => x.ConversationId);
    }
}
