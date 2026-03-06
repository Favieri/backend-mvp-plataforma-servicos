using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Review");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.OrderId).HasColumnName("orderId").IsRequired();
        builder.Property(x => x.ProfessionalId).HasColumnName("professionalId").IsRequired();
        builder.Property(x => x.ClientId).HasColumnName("clientId").IsRequired();
        builder.Property(x => x.Rating).HasColumnName("rating").IsRequired();
        builder.Property(x => x.Comment).HasColumnName("comment");
        // timestamp with time zone — legacy mode maps it to DateTime
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt").IsRequired();

        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => x.ProfessionalId);
        builder.HasIndex(x => new { x.ClientId, x.CreatedAt });
    }
}
