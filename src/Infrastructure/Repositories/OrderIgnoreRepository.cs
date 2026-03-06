using Application.Abstractions;
using Dapper;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class OrderIgnoreRepository(IConnectionFactory factory) : IOrderIgnoreRepository
{
    public async Task UpsertAsync(string professionalId, string orderId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "insert into \"ProfessionalOrderIgnore\"(\"professionalId\",\"orderId\",\"createdAt\") values(@professionalId,@orderId,now()) on conflict do nothing",
            new { professionalId, orderId }, cancellationToken: ct));
    }

    public async Task DeleteAsync(string professionalId, string orderId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "delete from \"ProfessionalOrderIgnore\" where \"professionalId\"=@professionalId and \"orderId\"=@orderId",
            new { professionalId, orderId }, cancellationToken: ct));
    }

    public async Task<bool> ProfessionalExistsAsync(string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(1) from \"Professional\" where id=@professionalId",
            new { professionalId }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> OrderExistsAsync(string orderId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(1) from \"Order\" where id=@orderId",
            new { orderId }, cancellationToken: ct)) > 0;
    }
}
