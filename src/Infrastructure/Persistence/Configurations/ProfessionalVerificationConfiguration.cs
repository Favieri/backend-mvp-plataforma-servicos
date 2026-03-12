using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ProfessionalVerificationConfiguration : IEntityTypeConfiguration<ProfessionalVerification>
{
    public void Configure(EntityTypeBuilder<ProfessionalVerification> builder)
    {
        builder.ToTable("professional_verification");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id").IsRequired();
        builder.Property(x => x.DocumentType).HasColumnName("document_type").IsRequired();
        builder.Property(x => x.DocumentUrl).HasColumnName("document_url").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasDefaultValue("submitted");
        builder.Property(x => x.Notes).HasColumnName("notes");
        builder.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.ProfessionalId);
        builder.HasIndex(x => new { x.Status, x.SubmittedAt });
    }
}
