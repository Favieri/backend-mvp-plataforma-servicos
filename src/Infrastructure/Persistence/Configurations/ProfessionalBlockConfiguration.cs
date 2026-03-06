using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ProfessionalBlockConfiguration : IEntityTypeConfiguration<ProfessionalBlock>
{
    public void Configure(EntityTypeBuilder<ProfessionalBlock> builder)
    {
        builder.ToTable("ProfessionalBlock");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ProfessionalId).HasColumnName("professionalId").IsRequired();
        // timestamp with time zone columns — Npgsql legacy mode maps them to DateTime
        builder.Property(x => x.StartsAt).HasColumnName("startsAt").IsRequired();
        builder.Property(x => x.EndsAt).HasColumnName("endsAt").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason");
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt").IsRequired();

        builder.HasIndex(x => new { x.ProfessionalId, x.StartsAt, x.EndsAt });
    }
}
