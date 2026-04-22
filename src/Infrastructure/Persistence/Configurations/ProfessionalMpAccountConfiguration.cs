using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ProfessionalMpAccountConfiguration : IEntityTypeConfiguration<ProfessionalMpAccount>
{
    public void Configure(EntityTypeBuilder<ProfessionalMpAccount> builder)
    {
        builder.ToTable("professional_mp_account");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id").IsRequired();
        builder.Property(x => x.MpUserId).HasColumnName("mp_user_id").IsRequired();
        builder.Property(x => x.MpAccessToken).HasColumnName("mp_access_token").IsRequired();
        builder.Property(x => x.MpRefreshToken).HasColumnName("mp_refresh_token").IsRequired();
        builder.Property(x => x.MpTokenExpiresAt).HasColumnName("mp_token_expires_at").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasDefaultValue("active");
        builder.Property(x => x.LiveMode).HasColumnName("live_mode").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // One account per professional
        builder.HasIndex(x => x.ProfessionalId)
            .IsUnique()
            .HasDatabaseName("IX_professional_mp_account_professional_id");

        // For MpTokenRefreshJob queries
        builder.HasIndex(x => new { x.Status, x.MpTokenExpiresAt })
            .HasDatabaseName("IX_professional_mp_account_status_expires");
    }
}
