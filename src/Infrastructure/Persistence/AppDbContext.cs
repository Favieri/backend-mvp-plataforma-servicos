using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Professional> Professionals => Set<Professional>();
    public DbSet<ProfessionalService> ProfessionalServices => Set<ProfessionalService>();
    public DbSet<ProfessionalZone> ProfessionalZones => Set<ProfessionalZone>();
    public DbSet<ProfessionalAvailability> ProfessionalAvailabilities => Set<ProfessionalAvailability>();
    public DbSet<ProfessionalBlock> ProfessionalBlocks => Set<ProfessionalBlock>();
    public DbSet<ProfessionalPortfolio> ProfessionalPortfolios => Set<ProfessionalPortfolio>();
    public DbSet<ProfessionalOrderIgnore> ProfessionalOrderIgnores => Set<ProfessionalOrderIgnore>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ServiceTier> ServiceTiers => Set<ServiceTier>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<Proposal> Proposals => Set<Proposal>();
    public DbSet<OrderTimeline> OrderTimelines => Set<OrderTimeline>();
    // Phase 2: chat transactional
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
