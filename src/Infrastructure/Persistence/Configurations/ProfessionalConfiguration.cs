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

        builder.Property(x => x.EntityType).HasColumnName("entityType");
        builder.Property(x => x.DocumentNumber).HasColumnName("documentNumber");
        builder.Property(x => x.YearsOfExperience).HasColumnName("yearsOfExperience");
        builder.Property(x => x.Specialties)
            .HasColumnName("specialties")
            .HasColumnType("text[]");
        builder.Property(x => x.ResponseRate).HasColumnName("responseRate");
        builder.Property(x => x.AvgResponseTimeMinutes).HasColumnName("avgResponseTimeMinutes");
        builder.Property(x => x.CompletionRate).HasColumnName("completionRate");
        builder.Property(x => x.VerificationStatus)
            .HasColumnName("verificationStatus")
            .HasDefaultValue("pending");
        builder.Property(x => x.Badges).HasColumnName("badges");
        builder.Property(x => x.BufferMinutes)
            .HasColumnName("bufferMinutes")
            .HasDefaultValue(0);

        builder.Property(x => x.MpConnected)
            .HasColumnName("mp_connected")
            .HasDefaultValue(false);
        builder.Property(x => x.MpConnectedAt)
            .HasColumnName("mp_connected_at");

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.Active, x.Rating });
    }
}
