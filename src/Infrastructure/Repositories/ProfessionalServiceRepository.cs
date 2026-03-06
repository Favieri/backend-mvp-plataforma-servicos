using Application.Abstractions;
using Dapper;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class ProfessionalServiceRepository(IConnectionFactory factory) : IProfessionalServiceRepository
{
    private const string SelectCols = """
        ps.id,ps."serviceId",ps."professionalId",ps."nomeServico",ps.preco,ps.descricao,
        s.id as "sid",s.name as "sname",s.icon as "sicon"
        """;

    private static object Map(dynamic row)
    {
        IDictionary<string, object?> r = row;
        return new
        {
            id = r["id"],
            serviceId = r["serviceId"],
            professionalId = r["professionalId"],
            nomeServico = r["nomeServico"],
            preco = r["preco"],
            descricao = r["descricao"],
            service = new { id = r["sid"], name = r["sname"], icon = r["sicon"] }
        };
    }

    public async Task<IReadOnlyList<object>> GetAsync(string? professionalId, string? serviceId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var where = "where 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(professionalId)) { where += " and ps.\"professionalId\"=@professionalId"; p.Add("professionalId", professionalId); }
        if (!string.IsNullOrWhiteSpace(serviceId)) { where += " and ps.\"serviceId\"=@serviceId"; p.Add("serviceId", serviceId); }
        var sql = $"""select {SelectCols} from "ProfessionalService" ps join "Service" s on s.id=ps."serviceId" {where} order by ps."nomeServico" asc""";
        var rows = await conn.QueryAsync(new CommandDefinition(sql, p, cancellationToken: ct));
        return rows.Select(r => Map(r)).ToList();
    }

    public async Task<object?> GetByIdAsync(string id, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var sql = $"""select {SelectCols} from "ProfessionalService" ps join "Service" s on s.id=ps."serviceId" where ps.id=@id""";
        var row = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    public async Task<object> CreateAsync(string professionalId, string serviceId, string nomeServico, decimal preco, string? descricao, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = """
            insert into "ProfessionalService"(id,"professionalId","serviceId","nomeServico",preco,descricao)
            values(gen_random_uuid()::text,@professionalId,@serviceId,@nomeServico,@preco,@descricao)
            returning id
            """;
        var id = await conn.ExecuteScalarAsync<string>(new CommandDefinition(sql, new { professionalId, serviceId, nomeServico, preco, descricao }, cancellationToken: ct));
        return (await GetByIdAsync(id!, ct))!;
    }

    public async Task<object?> UpdateAsync(string id, string? nomeServico, decimal? preco, string? descricao, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var setClauses = new List<string>();
        var p = new DynamicParameters();
        p.Add("id", id);
        if (nomeServico is not null) { setClauses.Add("\"nomeServico\"=@nomeServico"); p.Add("nomeServico", nomeServico); }
        if (preco is not null) { setClauses.Add("preco=@preco"); p.Add("preco", preco); }
        if (descricao is not null) { setClauses.Add("descricao=@descricao"); p.Add("descricao", descricao); }
        if (setClauses.Count == 0) return await GetByIdAsync(id, ct);
        var sql = $"update \"ProfessionalService\" set {string.Join(",", setClauses)} where id=@id";
        await conn.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct));
        return await GetByIdAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition("delete from \"ProfessionalService\" where id=@id", new { id }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> ProfessionalExistsAsync(string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition("select count(1) from \"Professional\" where id=@professionalId", new { professionalId }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> ServiceExistsAsync(string serviceId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition("select count(1) from \"Service\" where id=@serviceId", new { serviceId }, cancellationToken: ct)) > 0;
    }
}
