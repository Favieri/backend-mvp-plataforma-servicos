using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ProfessionalZoneConfiguration : IEntityTypeConfiguration<ProfessionalZone>
{
    public void Configure(EntityTypeBuilder<ProfessionalZone> builder)
    {
        builder.ToTable("ProfessionalZone");

        builder.HasKey(x => new { x.ProfessionalId, x.ZoneId });
        builder.Property(x => x.ProfessionalId).HasColumnName("professionalId").IsRequired();
        builder.Property(x => x.ZoneId).HasColumnName("zoneId").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt").IsRequired();

        builder.HasIndex(x => x.ZoneId);
    }
}
