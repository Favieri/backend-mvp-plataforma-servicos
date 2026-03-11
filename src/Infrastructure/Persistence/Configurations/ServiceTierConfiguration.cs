using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ServiceTierConfiguration : IEntityTypeConfiguration<ServiceTier>
{
    public void Configure(EntityTypeBuilder<ServiceTier> builder)
    {
        builder.ToTable("service_tier");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Name).HasColumnName("name").IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").IsRequired();
        builder.Property(x => x.AllowBookingDirect).HasColumnName("allow_booking_direct").IsRequired();
        builder.Property(x => x.RequiresProposal).HasColumnName("requires_proposal").IsRequired();
        builder.Property(x => x.RequiresChat).HasColumnName("requires_chat").IsRequired();
        builder.Property(x => x.AllowedPriceFormats)
            .HasColumnName("allowed_price_formats")
            .HasColumnType("text[]");
        builder.Property(x => x.DefaultSignalPercent).HasColumnName("default_signal_percent").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.MaxInstallments).HasColumnName("max_installments").IsRequired().HasDefaultValue(1);
        builder.Property(x => x.CancellationRules)
            .HasColumnName("cancellation_rules")
            .HasColumnType("jsonb");

        builder.HasIndex(x => x.Code).IsUnique();
    }
}
