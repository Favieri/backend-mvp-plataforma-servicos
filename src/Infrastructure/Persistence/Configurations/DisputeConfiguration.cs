using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> builder)
    {
        builder.ToTable("dispute");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.EvidenceUrls).HasColumnName("evidence_urls").HasColumnType("jsonb");
        builder.Property(x => x.ProfessionalResponse).HasColumnName("professional_response");
        builder.Property(x => x.ProfessionalEvidenceUrls).HasColumnName("professional_evidence_urls").HasColumnType("jsonb");
        builder.Property(x => x.Resolution).HasColumnName("resolution");
        builder.Property(x => x.ResolvedBy).HasColumnName("resolved_by");
        builder.Property(x => x.RefundAmountCents).HasColumnName("refund_amount_cents");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");

        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => x.ProfessionalId);
        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => x.Status);
    }
}
