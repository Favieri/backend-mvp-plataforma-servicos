using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversation");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.OrderId).HasColumnName("orderId");
        builder.Property(x => x.ClientId).HasColumnName("clientId").IsRequired();
        builder.Property(x => x.ProfessionalId).HasColumnName("professionalId").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt").IsRequired();
        // timestamp with time zone columns — legacy mode maps them to DateTime
        builder.Property(x => x.ClientLastReadAt).HasColumnName("clientLastReadAt");
        builder.Property(x => x.ProfessionalLastReadAt).HasColumnName("professionalLastReadAt");

        // Phase 2: conversation status
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasDefaultValue("active");

        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => x.ProfessionalId);
        builder.HasIndex(x => new { x.ClientId, x.ProfessionalId });
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => x.Status);
    }
}
