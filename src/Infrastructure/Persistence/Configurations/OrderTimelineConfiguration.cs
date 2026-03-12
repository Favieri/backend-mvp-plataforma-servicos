using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class OrderTimelineConfiguration : IEntityTypeConfiguration<OrderTimeline>
{
    public void Configure(EntityTypeBuilder<OrderTimeline> builder)
    {
        builder.ToTable("order_timeline");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.EventType).HasColumnName("event_type").IsRequired();
        builder.Property(x => x.ActorId).HasColumnName("actor_id");
        builder.Property(x => x.ActorRole).HasColumnName("actor_role");
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.OrderId, x.CreatedAt });
        builder.HasIndex(x => x.EventType);
    }
}
