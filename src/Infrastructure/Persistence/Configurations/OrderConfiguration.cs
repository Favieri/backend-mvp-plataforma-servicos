using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Order");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ClientId).HasColumnName("clientId").IsRequired();
        builder.Property(x => x.ServiceId).HasColumnName("serviceId").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.Location).HasColumnName("location");
        builder.Property(x => x.Date).HasColumnName("date");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasDefaultValue("aberto");
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt").IsRequired();

        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => x.ServiceId);
        builder.HasIndex(x => new { x.ClientId, x.CreatedAt });
        builder.HasIndex(x => x.Status);
    }
}
