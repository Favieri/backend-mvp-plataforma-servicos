using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ProfessionalConfiguration : IEntityTypeConfiguration<Professional>
{
    public void Configure(EntityTypeBuilder<Professional> builder)
    {
        builder.ToTable("Professional");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.UserId).HasColumnName("userId").IsRequired();
        builder.Property(x => x.Bio).HasColumnName("bio");
        builder.Property(x => x.Rating).HasColumnName("rating");
        builder.Property(x => x.Active).HasColumnName("active").IsRequired().HasDefaultValue(true);
        builder.Property(x => x.AvatarUrl).HasColumnName("avatarUrl");
        builder.Property(x => x.AvailabilityText).HasColumnName("availabilityText");
        builder.Property(x => x.CompletedJobsCount).HasColumnName("completedJobsCount").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.SlotMinutes).HasColumnName("slotMinutes");
        builder.Property(x => x.LeadTimeMinutes).HasColumnName("leadTimeMinutes");
        builder.Property(x => x.MaxAdvanceDays).HasColumnName("maxAdvanceDays");
        builder.Property(x => x.AllowInstantBooking).HasColumnName("allowInstantBooking");

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.Active, x.Rating });
    }
}
