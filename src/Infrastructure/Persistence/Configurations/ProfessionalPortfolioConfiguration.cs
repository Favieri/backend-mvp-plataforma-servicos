using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ProfessionalPortfolioConfiguration : IEntityTypeConfiguration<ProfessionalPortfolio>
{
    public void Configure(EntityTypeBuilder<ProfessionalPortfolio> builder)
    {
        builder.ToTable("ProfessionalPortfolio");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ProfessionalId).HasColumnName("professionalId").IsRequired();
        builder.Property(x => x.ImageUrl).HasColumnName("imageUrl").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title");
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.OrderIndex).HasColumnName("orderIndex");
        // timestamp with time zone — Npgsql legacy mode maps it to DateTime
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt").IsRequired();

        builder.HasIndex(x => x.ProfessionalId);
    }
}
