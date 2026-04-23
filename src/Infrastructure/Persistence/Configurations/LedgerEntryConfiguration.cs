using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entry");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Type).HasColumnName("type").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.Property(x => x.PaymentId).HasColumnName("payment_id");
        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id");
        builder.Property(x => x.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => x.OrderId)
               .HasDatabaseName("idx_ledger_order_id")
               .HasFilter("order_id IS NOT NULL");

        builder.HasIndex(x => new { x.Type, x.CreatedAt })
               .HasDatabaseName("idx_ledger_type_created");
    }
}
