using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RecurringPlanConfiguration : IEntityTypeConfiguration<RecurringPlan>
{
    public void Configure(EntityTypeBuilder<RecurringPlan> builder)
    {
        builder.ToTable("recurring_plan");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id").IsRequired();
        builder.Property(x => x.ServiceId).HasColumnName("service_id").IsRequired();
        builder.Property(x => x.SourceOrderId).HasColumnName("source_order_id").IsRequired();
        builder.Property(x => x.Frequency).HasColumnName("frequency").IsRequired();
        builder.Property(x => x.PriceTotalCents).HasColumnName("price_total_cents").IsRequired();
        builder.Property(x => x.DiscountPercent).HasColumnName("discount_percent").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.PaymentMethod).HasColumnName("payment_method");
        builder.Property(x => x.Scope).HasColumnName("scope");
        builder.Property(x => x.AddressId).HasColumnName("address_id");

        // ─── Service address snapshot ─────────────────────────────────────────
        builder.Property(x => x.SvcAddrZipCode).HasColumnName("svcAddrZipCode");
        builder.Property(x => x.SvcAddrStreet).HasColumnName("svcAddrStreet");
        builder.Property(x => x.SvcAddrNumber).HasColumnName("svcAddrNumber");
        builder.Property(x => x.SvcAddrNeighborhood).HasColumnName("svcAddrNeighborhood");
        builder.Property(x => x.SvcAddrCity).HasColumnName("svcAddrCity");
        builder.Property(x => x.SvcAddrState).HasColumnName("svcAddrState");
        builder.Property(x => x.SvcAddrComplement).HasColumnName("svcAddrComplement");
        builder.Property(x => x.SvcAddrReference).HasColumnName("svcAddrReference");

        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.NextBillingAt).HasColumnName("next_billing_at").IsRequired();
        builder.Property(x => x.OccurrenceCount).HasColumnName("occurrence_count").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.PausedAt).HasColumnName("paused_at");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        // Computed property — not mapped to a column
        builder.Ignore(x => x.EffectivePriceCents);

        builder.HasMany(x => x.Occurrences)
               .WithOne(o => o.Plan)
               .HasForeignKey(o => o.PlanId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.NextBillingAt, x.Status });
        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => x.ProfessionalId);
    }
}
