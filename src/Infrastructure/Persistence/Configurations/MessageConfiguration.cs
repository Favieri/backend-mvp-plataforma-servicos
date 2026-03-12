using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Message");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ConversationId).HasColumnName("conversationId").IsRequired();
        builder.Property(x => x.SenderId).HasColumnName("senderId").IsRequired();
        builder.Property(x => x.Text).HasColumnName("text").IsRequired();
        builder.Property(x => x.SentAt).HasColumnName("sentAt").IsRequired();

        // Phase 2: transactional chat fields
        builder.Property(x => x.Type).HasColumnName("type").IsRequired().HasDefaultValue("text");
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(x => x.ReplyToId).HasColumnName("replyToId");

        builder.HasIndex(x => x.ConversationId);
        builder.HasIndex(x => new { x.ConversationId, x.SentAt });
        builder.HasIndex(x => x.Type);
    }
}
