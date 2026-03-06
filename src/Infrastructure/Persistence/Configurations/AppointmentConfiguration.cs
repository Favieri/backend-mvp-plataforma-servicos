using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointment");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ProfessionalId).HasColumnName("professionalId").IsRequired();
        builder.Property(x => x.ClientId).HasColumnName("clientId");
        builder.Property(x => x.ServiceId).HasColumnName("serviceId");
        builder.Property(x => x.StartsAt).HasColumnName("startsAt").IsRequired();
        builder.Property(x => x.EndsAt).HasColumnName("endsAt").IsRequired();
        // AppointmentStatus is a USER-DEFINED PG enum; stored as text from C# side
        builder.Property(x => x.Status).HasColumnName("status").IsRequired();
        builder.Property(x => x.Location).HasColumnName("location");
        builder.Property(x => x.Notes).HasColumnName("notes");

        // createdAt / updatedAt columns exist in the DB but are not in the domain entity
        builder.HasIndex(x => x.ProfessionalId);
        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => new { x.ProfessionalId, x.StartsAt, x.Status });
    }
}
