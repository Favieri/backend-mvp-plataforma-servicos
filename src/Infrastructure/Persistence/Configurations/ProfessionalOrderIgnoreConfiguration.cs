using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ProfessionalOrderIgnoreConfiguration : IEntityTypeConfiguration<ProfessionalOrderIgnore>
{
    public void Configure(EntityTypeBuilder<ProfessionalOrderIgnore> builder)
    {
        builder.ToTable("ProfessionalOrderIgnore");

        builder.HasKey(x => new { x.ProfessionalId, x.OrderId });
        builder.Property(x => x.ProfessionalId).HasColumnName("professionalId").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("orderId").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt").IsRequired();
    }
}
