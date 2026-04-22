using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Order");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ClientId).HasColumnName("clientId").IsRequired();
        builder.Property(x => x.ServiceId).HasColumnName("serviceId").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.Location).HasColumnName("location");
        builder.Property(x => x.Date).HasColumnName("date");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasDefaultValue("aberto");
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt").IsRequired();

        // ─── Phase 1: transactional model ───────────────────────────────────
        builder.Property(x => x.ProfessionalId).HasColumnName("professionalId");
        builder.Property(x => x.TierId).HasColumnName("tierId");
        builder.Property(x => x.Origin).HasColumnName("origin");
        builder.Property(x => x.ProposalId).HasColumnName("proposalId");
        builder.Property(x => x.AppointmentId).HasColumnName("appointmentId");
        builder.Property(x => x.ConversationId).HasColumnName("conversationId");
        builder.Property(x => x.PriceTotalCents).HasColumnName("priceTotalCents");
        builder.Property(x => x.SignalCents).HasColumnName("signalCents");
        builder.Property(x => x.BalanceCents).HasColumnName("balanceCents");
        builder.Property(x => x.Installments).HasColumnName("installments").HasDefaultValue(1);
        builder.Property(x => x.PaymentMethod).HasColumnName("paymentMethod");
        builder.Property(x => x.AddressId).HasColumnName("addressId");
        builder.Property(x => x.Scope).HasColumnName("scope");
        builder.Property(x => x.ScheduledAt).HasColumnName("scheduledAt");
        builder.Property(x => x.CompletedAt).HasColumnName("completedAt");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelledAt");
        builder.Property(x => x.CancelledBy).HasColumnName("cancelledBy");
        builder.Property(x => x.CancellationReason).HasColumnName("cancellationReason");
        builder.Property(x => x.AutoConfirmAt).HasColumnName("autoConfirmAt");

        // ─── Service address snapshot ─────────────────────────────────────────
        builder.Property(x => x.SvcAddrZipCode).HasColumnName("svcAddrZipCode");
        builder.Property(x => x.SvcAddrStreet).HasColumnName("svcAddrStreet");
        builder.Property(x => x.SvcAddrNumber).HasColumnName("svcAddrNumber");
        builder.Property(x => x.SvcAddrNeighborhood).HasColumnName("svcAddrNeighborhood");
        builder.Property(x => x.SvcAddrCity).HasColumnName("svcAddrCity");
        builder.Property(x => x.SvcAddrState).HasColumnName("svcAddrState");
        builder.Property(x => x.SvcAddrComplement).HasColumnName("svcAddrComplement");
        builder.Property(x => x.SvcAddrReference).HasColumnName("svcAddrReference");

        // ─── Phase 4: recurring + rebook ────────────────────────────────────
        builder.Property(x => x.RecurringPlanId).HasColumnName("recurringPlanId");
        builder.HasIndex(x => x.RecurringPlanId).HasFilter("\"recurringPlanId\" IS NOT NULL");

        // ─── MP Integration ──────────────────────────────────────────────────
        builder.Property(x => x.PlatformFeePercent).HasColumnName("platformFeePercent").HasColumnType("numeric(5,2)").HasDefaultValue(10.00m);
        builder.Property(x => x.PlatformFeeCents).HasColumnName("platformFeeCents").HasDefaultValue(0);
        builder.Property(x => x.GatewayFeeCents).HasColumnName("gatewayFeeCents").HasDefaultValue(0);
        builder.Property(x => x.PaymentStatus).HasColumnName("paymentStatus").HasDefaultValue("unpaid");
        builder.Property(x => x.MpPreferenceId).HasColumnName("mpPreferenceId");
        // Supports webhook → order correlation by preferenceId
        builder.HasIndex(x => x.MpPreferenceId).HasFilter("\"mpPreferenceId\" IS NOT NULL");

        // ─── Indexes ────────────────────────────────────────────────────────
        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => x.ServiceId);
        builder.HasIndex(x => new { x.ClientId, x.CreatedAt });
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ProfessionalId);
        builder.HasIndex(x => x.AutoConfirmAt);
    }
}
