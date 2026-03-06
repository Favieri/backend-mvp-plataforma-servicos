using Application.Abstractions;
using Dapper;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class ReviewRepository(IConnectionFactory factory) : IReviewRepository
{
    public async Task<object> GetByProfessionalAsync(string professionalId, int limit, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var clampedLimit = Math.Max(1, Math.Min(limit, 50));
        var reviews = (await conn.QueryAsync(new CommandDefinition(
            """
            select r.id,r.rating,r.comment,r."createdAt",u.name as "clientName"
            from "Review" r left join "User" u on u.id=r."clientId"
            where r."professionalId"=@professionalId
            order by r."createdAt" desc limit @limit
            """,
            new { professionalId, limit = clampedLimit }, cancellationToken: ct))).ToList();

        var agg = await conn.QuerySingleAsync(new CommandDefinition(
            "select coalesce(avg(rating),0) as avg, count(*) as cnt from \"Review\" where \"professionalId\"=@professionalId",
            new { professionalId }, cancellationToken: ct));

        return new
        {
            reviews = reviews.Select(r => { IDictionary<string, object?> d = r; return new { id = d["id"], rating = d["rating"], comment = d["comment"], createdAt = d["createdAt"], clientName = d["clientName"] ?? "Cliente" }; }),
            average = Convert.ToDouble(((IDictionary<string, object?>)(dynamic)agg)["avg"]),
            count = Convert.ToInt64(((IDictionary<string, object?>)(dynamic)agg)["cnt"])
        };
    }

    public async Task<object?> GetByIdAsync(string id, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
            "select id,\"orderId\",\"professionalId\",\"clientId\",rating,comment,\"createdAt\" from \"Review\" where id=@id",
            new { id }, cancellationToken: ct));
    }

    public async Task<object> CreateAsync(string professionalId, string clientId, string orderId, int rating, string? comment, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = """
            insert into "Review"(id,"orderId","professionalId","clientId",rating,comment,"createdAt")
            values(gen_random_uuid()::text,@orderId,@professionalId,@clientId,@rating,@comment,now())
            returning id,rating,comment,"createdAt"
            """;
        var created = await conn.QuerySingleAsync(new CommandDefinition(sql,
            new { orderId, professionalId, clientId, rating, comment }, cancellationToken: ct));

        // Recalculate avg
        var agg = await conn.QuerySingleAsync(new CommandDefinition(
            "select coalesce(avg(rating),0) as avg, count(*) as cnt from \"Review\" where \"professionalId\"=@professionalId",
            new { professionalId }, cancellationToken: ct));
        double average = Convert.ToDouble(((IDictionary<string, object?>)(dynamic)agg)["avg"]);
        long count = Convert.ToInt64(((IDictionary<string, object?>)(dynamic)agg)["cnt"]);

        // Update professional rating
        await conn.ExecuteAsync(new CommandDefinition(
            "update \"Professional\" set rating=@rating where id=@professionalId",
            new { rating = average, professionalId }, cancellationToken: ct));

        IDictionary<string, object?> c = created;
        return new { ok = true, review = new { id = c["id"], rating = c["rating"], comment = c["comment"], createdAt = c["createdAt"] }, average, count };
    }

    public async Task<object?> UpdateAsync(string id, int? rating, string? comment, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var existing = await conn.QuerySingleOrDefaultAsync<(string ProfessionalId, string Id)>(new CommandDefinition(
            "select \"professionalId\" as ProfessionalId, id as Id from \"Review\" where id=@id",
            new { id }, cancellationToken: ct));
        if (existing == default) return null;

        var setClauses = new List<string>();
        var p = new DynamicParameters();
        p.Add("id", id);
        if (rating is not null) { setClauses.Add("rating=@rating"); p.Add("rating", rating); }
        if (comment is not null) { setClauses.Add("comment=@comment"); p.Add("comment", comment); }
        if (setClauses.Count > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition($"update \"Review\" set {string.Join(",", setClauses)} where id=@id", p, cancellationToken: ct));

            // Recalculate avg
            var agg = await conn.QuerySingleAsync(new CommandDefinition(
                "select coalesce(avg(r.rating),0) as avg from \"Review\" r where r.\"professionalId\"=@professionalId",
                new { professionalId = existing.ProfessionalId }, cancellationToken: ct));
            double avg = Convert.ToDouble(((IDictionary<string, object?>)(dynamic)agg)["avg"]);
            await conn.ExecuteAsync(new CommandDefinition(
                "update \"Professional\" set rating=@avg where id=@professionalId",
                new { avg, professionalId = existing.ProfessionalId }, cancellationToken: ct));
        }

        return await GetByIdAsync(id, ct);
    }

    public async Task<bool> OrderAlreadyReviewedAsync(string orderId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(1) from \"Review\" where \"orderId\"=@orderId",
            new { orderId }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> OrderBelongsToClientAsync(string orderId, string clientId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(1) from \"Order\" where id=@orderId and \"clientId\"=@clientId",
            new { orderId, clientId }, cancellationToken: ct)) > 0;
    }

    public async Task<string?> GetProfessionalUserIdAsync(string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "select \"userId\" from \"Professional\" where id=@professionalId",
            new { professionalId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<object>> GetEligibleOrdersAsync(string clientId, string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const int ReviewWindowDays = 14;
        var since = DateTime.UtcNow.AddDays(-ReviewWindowDays);

        var rows = (await conn.QueryAsync(new CommandDefinition(
            """
            select o.id,o."createdAt",o.date as "scheduledAt",o.status,s.name as "serviceName"
            from "Order" o
            left join "Service" s on s.id=o."serviceId"
            where o."clientId"=@clientId
              and o.status in ('concluido','auto_concluido')
              and o."createdAt" >= @since
              and not exists (select 1 from "Review" r where r."orderId"=o.id)
            order by o."createdAt" desc
            """,
            new { clientId, since }, cancellationToken: ct))).ToList();

        return rows.Select(r =>
        {
            IDictionary<string, object?> d = r;
            return (object)new
            {
                id = d["id"],
                createdAt = d["createdAt"],
                scheduledAt = d["scheduledAt"],
                status = d["status"],
                serviceName = d["serviceName"] ?? "Serviço"
            };
        }).ToList();
    }
}
