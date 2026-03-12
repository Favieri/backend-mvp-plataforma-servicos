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

        // ─── Phase 3: expanded categories ───────────────────────────────────
        builder.Property(x => x.PunctualityRating).HasColumnName("punctualityRating");
        builder.Property(x => x.QualityRating).HasColumnName("qualityRating");
        builder.Property(x => x.CommunicationRating).HasColumnName("communicationRating");
        builder.Property(x => x.CleanlinessRating).HasColumnName("cleanlinessRating");

        // ─── Phase 3: photos ─────────────────────────────────────────────────
        builder.Property(x => x.PhotoUrls).HasColumnName("photoUrls").HasColumnType("jsonb");

        // ─── Phase 3: professional reviews client ────────────────────────────
        builder.Property(x => x.ProfessionalReviewOfClient).HasColumnName("professionalReviewOfClient");
        builder.Property(x => x.ProfessionalRatingOfClient).HasColumnName("professionalRatingOfClient");

        // ─── Phase 3: double-blind ───────────────────────────────────────────
        builder.Property(x => x.ClientVisibleAt).HasColumnName("clientVisibleAt");
        builder.Property(x => x.ProfessionalVisibleAt).HasColumnName("professionalVisibleAt");

        // ─── Phase 3: verified ───────────────────────────────────────────────
        builder.Property(x => x.IsVerified).HasColumnName("isVerified").HasDefaultValue(false);

        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => x.ProfessionalId);
        builder.HasIndex(x => new { x.ClientId, x.CreatedAt });
    }
}
