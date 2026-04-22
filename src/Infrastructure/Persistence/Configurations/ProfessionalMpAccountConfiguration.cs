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
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid");

        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id").IsRequired();
        builder.Property(x => x.MpUserId).HasColumnName("mp_user_id").IsRequired();
        builder.Property(x => x.MpAccessToken).HasColumnName("mp_access_token").IsRequired();
        builder.Property(x => x.MpRefreshToken).HasColumnName("mp_refresh_token").IsRequired();
        builder.Property(x => x.MpTokenExpiresAt).HasColumnName("mp_token_expires_at").IsRequired();
        builder.Property(x => x.MpScope).HasColumnName("mp_scope");
        builder.Property(x => x.MpLiveMode).HasColumnName("mp_live_mode").IsRequired().HasDefaultValue(false);
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasDefaultValue("active");
        builder.Property(x => x.ConnectedAt).HasColumnName("connected_at").IsRequired();
        builder.Property(x => x.LastRefreshedAt).HasColumnName("last_refreshed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // One professional → at most one MP account (UNIQUE enforced in DB)
        builder.HasIndex(x => x.ProfessionalId).IsUnique();
        // Supports the token-refresh background job
        builder.HasIndex(x => new { x.MpTokenExpiresAt, x.Status })
               .HasFilter("status = 'active'");
    }
}
