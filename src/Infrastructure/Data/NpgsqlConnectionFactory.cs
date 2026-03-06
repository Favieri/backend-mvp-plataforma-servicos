using System.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Infrastructure.Data;

public interface IConnectionFactory
{
    Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken ct);
}

public sealed class NpgsqlConnectionFactory : IConnectionFactory, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlConnectionFactory(IOptions<DatabaseOptions> options, IHostEnvironment environment)
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(options.Value.ConnectionString)
        {
            Timeout = options.Value.TimeoutSeconds,
            CommandTimeout = options.Value.CommandTimeoutSeconds,
            MaxPoolSize = options.Value.MaximumPoolSize,
            Pooling = true,
            NoResetOnClose = true
        };

        if (!environment.IsDevelopment() && ShouldUseSupabasePooler(connectionStringBuilder))
        {
            connectionStringBuilder.Port = options.Value.PoolerPort;
        }

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionStringBuilder.ConnectionString);
        _dataSource = dataSourceBuilder.Build();
    }

    public async Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken ct)
    {
        return await _dataSource.OpenConnectionAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
    }

    internal static bool ShouldUseSupabasePooler(NpgsqlConnectionStringBuilder builder)
    {
        return !string.IsNullOrWhiteSpace(builder.Host)
               && builder.Host.Contains("supabase", StringComparison.OrdinalIgnoreCase)
               && builder.Port != 6543;
    }
}
