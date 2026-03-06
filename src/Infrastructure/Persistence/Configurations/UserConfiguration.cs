using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("User");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Name).HasColumnName("name").IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").IsRequired();
        builder.Property(x => x.Phone).HasColumnName("phone");
        builder.Property(x => x.Role).HasColumnName("role").IsRequired();
        builder.Property(x => x.ZoneId).HasColumnName("zoneId");
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt").IsRequired();

        // senha is stored in the DB but not part of the User domain entity
        // accessed only by AuthRepository via raw projection
        builder.Ignore("Senha");

        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasIndex(x => x.ZoneId);
    }
}
