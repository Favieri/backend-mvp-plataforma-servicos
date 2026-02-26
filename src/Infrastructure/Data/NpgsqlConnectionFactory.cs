using System.Data;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Infrastructure.Data;

public interface IConnectionFactory
{
    Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken ct);
}

public sealed class NpgsqlConnectionFactory(IOptions<DatabaseOptions> options) : IConnectionFactory
{
    public async Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(options.Value.ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
