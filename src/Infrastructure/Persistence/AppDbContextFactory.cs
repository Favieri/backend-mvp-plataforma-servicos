using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by dotnet-ef CLI to create migrations.
/// Connection string is read from the DB_CONNECTION environment variable.
/// In production, never hardcode real credentials here.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Required for Npgsql timestamp compatibility (DateTime instead of DateTimeOffset for timestamptz)
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION")
            ?? "Host=localhost;Database=jobeasy;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
