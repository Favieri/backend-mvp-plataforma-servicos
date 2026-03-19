using System.Data;
using Npgsql;

namespace Infrastructure.Data;

public interface IConnectionFactory
{
    Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken ct);
}

/// <summary>
/// Dapper connection factory. Receives the application-wide shared <see cref="NpgsqlDataSource"/>
/// so that EF Core and Dapper use a single connection pool per process/Lambda instance.
/// The data source lifetime is managed by the DI container; this class must not dispose it.
/// </summary>
public sealed class NpgsqlConnectionFactory : IConnectionFactory
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlConnectionFactory(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken ct)
    {
        return await _dataSource.OpenConnectionAsync(ct);
    }

    internal static bool IsSupabaseHost(NpgsqlConnectionStringBuilder builder)
    {
        return !string.IsNullOrWhiteSpace(builder.Host)
               && builder.Host.Contains("supabase", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldUseSupabasePooler(NpgsqlConnectionStringBuilder builder)
    {
        return IsSupabaseHost(builder) && builder.Port != 6543;
    }
}
