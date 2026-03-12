using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ProposalConfiguration : IEntityTypeConfiguration<Proposal>
{
    public void Configure(EntityTypeBuilder<Proposal> builder)
    {
        builder.ToTable("proposal");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id").IsRequired();
        builder.Property(x => x.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(x => x.ServiceId).HasColumnName("service_id").IsRequired();
        builder.Property(x => x.ProfessionalServiceId).HasColumnName("professional_service_id");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.Scope).HasColumnName("scope").IsRequired();
        builder.Property(x => x.IncludesDescription).HasColumnName("includes_description");
        builder.Property(x => x.ExcludesDescription).HasColumnName("excludes_description");
        builder.Property(x => x.PriceTotalCents).HasColumnName("price_total_cents").IsRequired();
        builder.Property(x => x.PriceByStage).HasColumnName("price_by_stage").HasColumnType("jsonb");
        builder.Property(x => x.DurationEstimate).HasColumnName("duration_estimate");
        builder.Property(x => x.SuggestedDatetime).HasColumnName("suggested_datetime");
        builder.Property(x => x.VisitFeeCents).HasColumnName("visit_fee_cents").HasDefaultValue(0);
        builder.Property(x => x.ValidUntil).HasColumnName("valid_until").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasDefaultValue("draft");
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.ProfessionalId);
        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => x.ConversationId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ValidUntil);
    }
}
