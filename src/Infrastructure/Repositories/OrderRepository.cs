using System.Data;
using Application.Abstractions;
using Dapper;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class OrderRepository(IConnectionFactory factory) : IOrderRepository
{
    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string? serviceId, string? excludeProfessionalId, string? professionalId, bool filterZones, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var where = " where 1=1 ";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(serviceId)) { where += " and o.\"serviceId\"=@serviceId"; p.Add("serviceId", serviceId); }
        if (filterZones && !string.IsNullOrWhiteSpace(professionalId))
        {
            where += " and o.\"clientId\" in (select u.id from \"User\" u where u.\"zoneId\" in (select pz.\"zoneId\" from \"ProfessionalZone\" pz where pz.\"professionalId\"=@professionalId))";
            p.Add("professionalId", professionalId);
        }
        if (!string.IsNullOrWhiteSpace(excludeProfessionalId))
        {
            where += " and o.id not in (select poi.\"orderId\" from \"ProfessionalOrderIgnore\" poi where poi.\"professionalId\"=@excludeProfessionalId)";
            p.Add("excludeProfessionalId", excludeProfessionalId);
        }
        var sql = $"select o.id AS \"Id\",o.\"clientId\" AS \"ClientId\",o.\"serviceId\" AS \"ServiceId\",o.description AS \"Description\",o.location AS \"Location\",o.date AS \"Date\",o.status AS \"Status\",o.\"createdAt\" AS \"CreatedAt\" from \"Order\" o {where} order by o.\"createdAt\" desc";
        var rows = await conn.QueryAsync<Order>(new CommandDefinition(sql, p, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Order?> GetByIdAsync(string id, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "select id AS \"Id\",\"clientId\" AS \"ClientId\",\"serviceId\" AS \"ServiceId\",description AS \"Description\",location AS \"Location\",date AS \"Date\",status AS \"Status\",\"createdAt\" AS \"CreatedAt\" from \"Order\" where id=@id";
        return await conn.QuerySingleOrDefaultAsync<Order>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<Order> CreateAsync(string clientId, string serviceId, string? description, string? location, DateTime? date, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "insert into \"Order\"(id,\"clientId\",\"serviceId\",description,location,date,status,\"createdAt\") values (gen_random_uuid()::text,@clientId,@serviceId,@description,@location,@date,@status,now()) returning id AS \"Id\",\"clientId\" AS \"ClientId\",\"serviceId\" AS \"ServiceId\",description AS \"Description\",location AS \"Location\",date AS \"Date\",status AS \"Status\",\"createdAt\" AS \"CreatedAt\"";
        return await conn.QuerySingleAsync<Order>(new CommandDefinition(sql, new { clientId, serviceId, description, location, date, status = OrderStatus.Aberto }, cancellationToken: ct));
    }

    public async Task CompleteOrderAsync(string orderId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "update \"Order\" set status=@status where id=@orderId";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { status = OrderStatus.Concluido, orderId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<object>> GetMineAsync(string clientId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "select id,status,date as \"scheduledAt\",\"createdAt\" as \"createdAt\" from \"Order\" where \"clientId\"=@clientId order by \"createdAt\" desc";
        var rows = await conn.QueryAsync(sql, new { clientId });
        return rows.ToList();
    }
}
