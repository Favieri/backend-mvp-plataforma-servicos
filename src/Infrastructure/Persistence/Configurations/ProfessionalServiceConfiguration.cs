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

        builder.HasIndex(x => x.ProfessionalId);
        builder.HasIndex(x => x.ServiceId);
    }
}
