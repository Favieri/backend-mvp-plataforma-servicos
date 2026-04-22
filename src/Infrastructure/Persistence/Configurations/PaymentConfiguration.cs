using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payment");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.Gateway).HasColumnName("gateway").IsRequired();
        builder.Property(x => x.GatewayRef).HasColumnName("gateway_ref");
        builder.Property(x => x.Method).HasColumnName("method").IsRequired().HasDefaultValue("unknown");
        builder.Property(x => x.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(x => x.PlatformFeeCents).HasColumnName("platform_fee_cents").HasDefaultValue(0);
        builder.Property(x => x.GatewayFeeCents).HasColumnName("gateway_fee_cents").HasDefaultValue(0);
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasDefaultValue("pending");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.PaidAt).HasColumnName("paid_at");
        builder.Property(x => x.PixCode).HasColumnName("pix_code");
        builder.Property(x => x.PixQrCodeBase64).HasColumnName("pix_qr_code_base64");
        builder.Property(x => x.PixExpiresAt).HasColumnName("pix_expires_at");

        builder.HasIndex(x => x.OrderId).HasDatabaseName("idx_payment_order_id");
        builder.HasIndex(x => new { x.OrderId, x.Status })
            .HasDatabaseName("idx_payment_order_status")
            .HasFilter("status = 'pending'");
    }
}
