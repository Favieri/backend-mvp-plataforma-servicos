using Application.Abstractions;
using Dapper;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class UserRepository(IConnectionFactory factory) : IUserRepository
{
    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "select count(1) from \"User\" where email=@email";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { email }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> ZoneExistsAndActiveAsync(string zoneId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "select count(1) from \"Zone\" where id=@zoneId and active=true";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { zoneId }, cancellationToken: ct)) > 0;
    }

    public async Task<object> CreateAsync(string name, string email, string? phone, string role, string hashedPassword, string? zoneId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = """
            insert into "User"(id,name,email,phone,role,senha,"zoneId","createdAt")
            values (gen_random_uuid()::text,@name,@email,@phone,@role,@senha,@zoneId,now())
            returning id,name,email,phone,role,"zoneId","createdAt" as "createdAt"
            """;
        return await conn.QuerySingleAsync(new CommandDefinition(sql,
            new { name, email, phone, role, senha = hashedPassword, zoneId },
            cancellationToken: ct));
    }
}
