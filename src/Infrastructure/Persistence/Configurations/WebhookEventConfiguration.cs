using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("webhook_events");

        // Composite PK guarantees idempotency: same event never processed twice
        builder.HasKey(x => new { x.Provider, x.EventId });
        builder.Property(x => x.Provider).HasColumnName("provider").IsRequired();
        builder.Property(x => x.EventId).HasColumnName("event_id").IsRequired();
        builder.Property(x => x.Topic).HasColumnName("topic").IsRequired();
        builder.Property(x => x.Action).HasColumnName("action");
        builder.Property(x => x.RawPayload).HasColumnName("raw_payload").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasDefaultValue("received");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");

        // Supports reprocessing job for failed/pending events
        builder.HasIndex(x => new { x.Status, x.CreatedAt })
               .HasFilter("status IN ('failed', 'received')");
    }
}
