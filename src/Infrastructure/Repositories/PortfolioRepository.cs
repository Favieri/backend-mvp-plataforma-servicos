using Application.Abstractions;
using Dapper;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class PortfolioRepository(IConnectionFactory factory) : IPortfolioRepository
{
    private const string SelectCols = """id,"professionalId","imageUrl",title,description,"orderIndex","createdAt" """;

    public async Task<IReadOnlyList<object>> GetByProfessionalAsync(string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var rows = await conn.QueryAsync(new CommandDefinition(
            $"select {SelectCols} from \"ProfessionalPortfolio\" where \"professionalId\"=@professionalId order by \"orderIndex\" asc nulls last, \"createdAt\" desc",
            new { professionalId }, cancellationToken: ct));
        return rows.Cast<object>().ToList();
    }

    public async Task<object?> GetByIdAsync(string id, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
            $"select {SelectCols} from \"ProfessionalPortfolio\" where id=@id",
            new { id }, cancellationToken: ct));
    }

    public async Task<object> CreateAsync(string professionalId, string imageUrl, string? title, string? description, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var nextIndex = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            "select max(\"orderIndex\") from \"ProfessionalPortfolio\" where \"professionalId\"=@professionalId",
            new { professionalId }, cancellationToken: ct));
        var orderIndex = (nextIndex ?? -1) + 1;

        const string sql = """
            insert into "ProfessionalPortfolio"(id,"professionalId","imageUrl",title,description,"orderIndex","createdAt")
            values(gen_random_uuid()::text,@professionalId,@imageUrl,@title,@description,@orderIndex,now())
            returning id,"professionalId","imageUrl",title,description,"orderIndex","createdAt"
            """;
        return await conn.QuerySingleAsync(new CommandDefinition(sql,
            new { professionalId, imageUrl, title, description, orderIndex }, cancellationToken: ct));
    }

    public async Task<object?> UpdateAsync(string id, string? title, string? description, string? imageUrl, int? orderIndex, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var setClauses = new List<string>();
        var p = new DynamicParameters();
        p.Add("id", id);
        if (title is not null) { setClauses.Add("title=@title"); p.Add("title", title); }
        if (description is not null) { setClauses.Add("description=@description"); p.Add("description", description); }
        if (imageUrl is not null) { setClauses.Add("\"imageUrl\"=@imageUrl"); p.Add("imageUrl", imageUrl); }
        if (orderIndex is not null) { setClauses.Add("\"orderIndex\"=@orderIndex"); p.Add("orderIndex", orderIndex); }
        if (setClauses.Count > 0)
            await conn.ExecuteAsync(new CommandDefinition($"update \"ProfessionalPortfolio\" set {string.Join(",", setClauses)} where id=@id", p, cancellationToken: ct));
        return await GetByIdAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition("delete from \"ProfessionalPortfolio\" where id=@id", new { id }, cancellationToken: ct));
        return affected > 0;
    }
}
