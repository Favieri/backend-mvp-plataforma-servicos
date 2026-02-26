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
        if (!string.IsNullOrWhiteSpace(serviceId)) { where += " and o.serviceid=@serviceId"; p.Add("serviceId", serviceId); }
        if (filterZones && !string.IsNullOrWhiteSpace(professionalId))
        {
            where += " and o.clientid in (select u.id from \"User\" u where u.zoneid in (select zoneid from \"ProfessionalZone\" where professionalid=@professionalId))";
            p.Add("professionalId", professionalId);
        }
        if (!string.IsNullOrWhiteSpace(excludeProfessionalId))
        {
            where += " and o.id not in (select orderid from \"ProfessionalOrderIgnore\" where professionalid=@excludeProfessionalId)";
            p.Add("excludeProfessionalId", excludeProfessionalId);
        }
        var sql = $"select o.id,o.clientid as ClientId,o.serviceid as ServiceId,o.description,o.location,o.date,o.status,o.createdat as CreatedAt from \"Order\" o {where} order by o.createdat desc";
        var rows = await conn.QueryAsync<Order>(new CommandDefinition(sql, p, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Order?> GetByIdAsync(string id, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "select id,clientid as ClientId,serviceid as ServiceId,description,location,date,status,createdat as CreatedAt from \"Order\" where id=@id";
        return await conn.QuerySingleOrDefaultAsync<Order>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<Order> CreateAsync(string clientId, string serviceId, string? description, string? location, DateTime? date, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "insert into \"Order\"(id,clientid,serviceid,description,location,date,status,createdat) values (gen_random_uuid()::text,@clientId,@serviceId,@description,@location,@date,@status,now()) returning id,clientid as ClientId,serviceid as ServiceId,description,location,date,status,createdat as CreatedAt";
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
        const string sql = "select id,status,date as scheduledAt,createdat as createdAt from \"Order\" where clientid=@clientId order by createdat desc";
        var rows = await conn.QueryAsync(sql, new { clientId });
        return rows.ToList();
    }
}
