using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ProfessionalServiceConfiguration : IEntityTypeConfiguration<ProfessionalService>
{
    public void Configure(EntityTypeBuilder<ProfessionalService> builder)
    {
        builder.ToTable("ProfessionalService");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ProfessionalId).HasColumnName("professionalId").IsRequired();
        builder.Property(x => x.ServiceId).HasColumnName("serviceId").IsRequired();
        builder.Property(x => x.NomeServico).HasColumnName("nomeServico").IsRequired();
        builder.Property(x => x.Preco).HasColumnName("preco").IsRequired();
        builder.Property(x => x.Descricao).HasColumnName("descricao");

        builder.Property(x => x.TierId).HasColumnName("tierId");
        builder.Property(x => x.ContractMode).HasColumnName("contractMode");
        builder.Property(x => x.DurationMinutes).HasColumnName("durationMinutes");
        builder.Property(x => x.IncludesDescription).HasColumnName("includesDescription");
        builder.Property(x => x.ExcludesDescription).HasColumnName("excludesDescription");
        builder.Property(x => x.MaterialIncluded).HasColumnName("materialIncluded");
        builder.Property(x => x.VisitFeeCents).HasColumnName("visitFeeCents");
        builder.Property(x => x.MinLeadTimeMinutes).HasColumnName("minLeadTimeMinutes");

        builder.HasIndex(x => x.ProfessionalId);
        builder.HasIndex(x => x.ServiceId);
    }
}
