using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RecurringOccurrenceConfiguration : IEntityTypeConfiguration<RecurringOccurrence>
{
    public void Configure(EntityTypeBuilder<RecurringOccurrence> builder)
    {
        builder.ToTable("recurring_occurrence");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.Property(x => x.OccurrenceNumber).HasColumnName("occurrence_number").IsRequired();
        builder.Property(x => x.ScheduledFor).HasColumnName("scheduled_for").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne(x => x.Plan)
               .WithMany(p => p.Occurrences)
               .HasForeignKey(x => x.PlanId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.PlanId, x.OccurrenceNumber }).IsUnique();
        builder.HasIndex(x => x.OrderId).HasFilter("order_id IS NOT NULL");
    }
}
