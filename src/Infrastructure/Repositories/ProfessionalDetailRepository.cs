using Application.Abstractions;
using Dapper;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class ProfessionalDetailRepository(IConnectionFactory factory) : IProfessionalDetailRepository
{
    public async Task<object?> GetByIdAsync(string id, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);

        // Professional + User
        const string sqlPro = """
            select p.id,p."userId",p.bio,p.rating,p.active,p."avatarUrl",p."availabilityText",
                   p."completedJobsCount",p."slotMinutes",p."leadTimeMinutes",p."maxAdvanceDays",p."allowInstantBooking",
                   u.id as "uid",u.name,u.email,u.phone,u.role,u."zoneId",u."createdAt"
            from "Professional" p join "User" u on u.id=p."userId"
            where p.id=@id
            """;
        var row = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(sqlPro, new { id }, cancellationToken: ct));
        if (row is null) return null;

        // Services
        var services = (await conn.QueryAsync(new CommandDefinition(
            """select id,"serviceId","professionalId","nomeServico",preco,descricao from "ProfessionalService" where "professionalId"=@id""",
            new { id }, cancellationToken: ct))).ToList();

        // Portfolio
        var portfolio = (await conn.QueryAsync(new CommandDefinition(
            """select id,"professionalId","imageUrl",title,description,"orderIndex","createdAt" from "ProfessionalPortfolio" where "professionalId"=@id order by "orderIndex" asc nulls last, "createdAt" desc""",
            new { id }, cancellationToken: ct))).ToList();

        // Zones with zone info
        var zones = (await conn.QueryAsync(new CommandDefinition(
            """select pz."professionalId",pz."zoneId",z.id as "zid",z.name as "zname",z.active as "zactive" from "ProfessionalZone" pz join "Zone" z on z.id=pz."zoneId" where pz."professionalId"=@id""",
            new { id }, cancellationToken: ct))).ToList();

        IDictionary<string, object?> r = row;
        return new
        {
            id = r["id"],
            userId = r["userId"],
            bio = r["bio"],
            rating = r["rating"],
            active = r["active"],
            avatarUrl = r["avatarUrl"],
            availabilityText = r["availabilityText"],
            completedJobsCount = r["completedJobsCount"],
            slotMinutes = r["slotMinutes"],
            leadTimeMinutes = r["leadTimeMinutes"],
            maxAdvanceDays = r["maxAdvanceDays"],
            allowInstantBooking = r["allowInstantBooking"],
            user = new { id = r["uid"], name = r["name"], email = r["email"], phone = r["phone"], role = r["role"], zoneId = r["zoneId"], createdAt = r["createdAt"] },
            services,
            portfolio,
            zones = zones.Select(z =>
            {
                IDictionary<string, object?> zd = z;
                return new
                {
                    professionalId = zd["professionalId"],
                    zoneId = zd["zoneId"],
                    zone = new { id = zd["zid"], name = zd["zname"], active = zd["zactive"] }
                };
            }).ToList()
        };
    }

    public async Task<object?> UpdateAsync(string id, string? bio, bool? active, string? availabilityText, string? avatarUrl, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);

        // Build dynamic SET
        var setClauses = new List<string>();
        var p = new DynamicParameters();
        p.Add("id", id);

        if (bio is not null) { setClauses.Add("bio=@bio"); p.Add("bio", bio); }
        if (active is not null) { setClauses.Add("active=@active"); p.Add("active", active); }
        if (availabilityText is not null) { setClauses.Add("\"availabilityText\"=@availabilityText"); p.Add("availabilityText", availabilityText); }
        if (avatarUrl is not null) { setClauses.Add("\"avatarUrl\"=@avatarUrl"); p.Add("avatarUrl", avatarUrl); }

        if (setClauses.Count == 0)
            return await GetByIdAsync(id, ct);

        var sql = $"update \"Professional\" set {string.Join(",", setClauses)} where id=@id";
        await conn.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct));
        return await GetByIdAsync(id, ct);
    }

    public async Task<IReadOnlyList<object>> GetZonesAsync(string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var rows = await conn.QueryAsync(new CommandDefinition(
            """select pz."professionalId",pz."zoneId",z.id as "zid",z.name as "zname",z.active as "zactive" from "ProfessionalZone" pz join "Zone" z on z.id=pz."zoneId" where pz."professionalId"=@professionalId""",
            new { professionalId }, cancellationToken: ct));
        return rows.Select(z =>
        {
            IDictionary<string, object?> zd = z;
            return (object)new
            {
                professionalId = zd["professionalId"],
                zoneId = zd["zoneId"],
                zone = new { id = zd["zid"], name = zd["zname"], active = zd["zactive"] }
            };
        }).ToList();
    }

    public async Task<object?> UpdateZonesAsync(string professionalId, string[] zoneIds, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);

        // Validate zones exist and are active
        if (zoneIds.Length > 0)
        {
            var found = (await conn.QueryAsync<string>(new CommandDefinition(
                "select id from \"Zone\" where id = any(@ids) and active=true",
                new { ids = zoneIds }, cancellationToken: ct))).ToHashSet();
            var invalid = zoneIds.Where(z => !found.Contains(z)).ToList();
            if (invalid.Count > 0)
                throw new InvalidOperationException($"Zonas inválidas/inativas: {string.Join(", ", invalid)}");
        }

        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(new CommandDefinition(
            "delete from \"ProfessionalZone\" where \"professionalId\"=@professionalId",
            new { professionalId }, transaction: tx, cancellationToken: ct));

        if (zoneIds.Length > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "insert into \"ProfessionalZone\"(\"professionalId\",\"zoneId\",\"createdAt\") select @professionalId,unnest(@zoneIds::text[]),now() on conflict do nothing",
                new { professionalId, zoneIds }, transaction: tx, cancellationToken: ct));
        }
        tx.Commit();

        return await GetByIdAsync(professionalId, ct);
    }
}
