using Application.Abstractions;
using Dapper;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class AuthRepository(IConnectionFactory factory) : IAuthRepository
{
    public async Task<object?> LoginAsync(string email, string password, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "select id,name,email,phone,role,senha,\"createdAt\" AS \"CreatedAt\" from \"User\" where email=@email";
        var row = await conn.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(sql, new { email }, cancellationToken: ct));
        if (row is null) return null;
        string hash = row.senha;
        var valid = BCrypt.Net.BCrypt.Verify(password, hash);
        if (!valid) return null;
        return new
        {
            id = (string)row.id,
            name = (string)row.name,
            email = (string)row.email,
            phone = (string?)row.phone,
            role = (string)row.role,
            createdAt = (DateTime)row.CreatedAt
        };
    }
}
