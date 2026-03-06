using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ProfessionalAvailabilityConfiguration : IEntityTypeConfiguration<ProfessionalAvailability>
{
    public void Configure(EntityTypeBuilder<ProfessionalAvailability> builder)
    {
        builder.ToTable("ProfessionalAvailability");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ProfessionalId).HasColumnName("professionalId").IsRequired();
        builder.Property(x => x.Weekday).HasColumnName("weekday").IsRequired();
        builder.Property(x => x.StartMinutes).HasColumnName("startMinutes").IsRequired();
        builder.Property(x => x.EndMinutes).HasColumnName("endMinutes").IsRequired();
        builder.Property(x => x.Active).HasColumnName("active").IsRequired().HasDefaultValue(true);

        builder.HasIndex(x => new { x.ProfessionalId, x.Weekday });
    }
}
