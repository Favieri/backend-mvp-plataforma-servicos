using System.Data;
using Application.Abstractions;
using Application.DTOs;
using Dapper;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class ProfessionalReadRepository(IConnectionFactory factory) : IProfessionalReadRepository
{
    public async Task<IReadOnlyList<ProfessionalCardDto>> GetProfessionalsAsync(string? zoneId, string? serviceId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);

        var where = "where p.\"active\" = true";
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(zoneId))
        {
            where += @"
                and exists (
                    select 1
                    from ""ProfessionalZone"" pz
                    where pz.""professionalId"" = p.""id""
                      and pz.""zoneId"" = @zoneId
                )";
            p.Add("zoneId", zoneId);
        }

        if (!string.IsNullOrWhiteSpace(serviceId))
        {
            where += @"
                and exists (
                    select 1
                    from ""ProfessionalService"" ps
                    where ps.""professionalId"" = p.""id""
                      and ps.""serviceId"" = @serviceId
                )";
            p.Add("serviceId", serviceId);
        }

        var baseSql = $@"
            select
                p.""id"" as ""Id"",
                p.""userId"" as ""UserId"",
                coalesce(u.""name"", u.""nome"", '') as ""Name"",
                coalesce(u.""avatarUrl"", p.""avatarUrl"") as ""AvatarUrl"",
                p.""rating"" as ""Rating"",
                coalesce(p.""active"", true) as ""Active"",
                coalesce(p.""completedJobsCount"", 0) as ""CompletedJobsCount"",
                p.""availabilityText"" as ""AvailabilityText""
            from ""Professional"" p
            join ""User"" u on u.""id"" = p.""userId""
            {where}
            order by p.""id""";

        var professionals = (await conn.QueryAsync<ProfessionalRow>(new CommandDefinition(baseSql, p, cancellationToken: ct))).ToList();
        if (professionals.Count == 0) return Array.Empty<ProfessionalCardDto>();

        var professionalIds = professionals.Select(x => x.Id).Distinct().ToArray();

        const string servicesSql = @"
            select
                ps.""professionalId"" as ""ProfessionalId"",
                ps.""id"" as ""Id"",
                ps.""serviceId"" as ""ServiceId"",
                ps.""nomeServico"" as ""Name"",
                coalesce(ps.""preco"", 0) as ""Price"",
                ps.""descricao"" as ""Description"",
                s.""icon"" as ""Icon""
            from ""ProfessionalService"" ps
            left join ""Service"" s on s.""id"" = ps.""serviceId""
            where ps.""professionalId"" = any(@professionalIds)";

        var serviceRows = await conn.QueryAsync<ProfessionalServiceRow>(new CommandDefinition(servicesSql, new { professionalIds }, cancellationToken: ct));

        const string zonesSql = @"
            select
                pz.""professionalId"" as ""ProfessionalId"",
                z.""id"" as ""Id"",
                z.""name"" as ""Name""
            from ""ProfessionalZone"" pz
            join ""Zone"" z on z.""id"" = pz.""zoneId""
            where pz.""professionalId"" = any(@professionalIds)
              and z.""active"" = true";

        var zoneRows = await conn.QueryAsync<ProfessionalZoneRow>(new CommandDefinition(zonesSql, new { professionalIds }, cancellationToken: ct));

        var servicesByProfessional = serviceRows
            .GroupBy(x => x.ProfessionalId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ProfessionalServiceDto>)g
                    .Select(x => new ProfessionalServiceDto(x.Id, x.ServiceId, x.Name ?? string.Empty, x.Price, x.Description, x.Icon))
                    .ToList());

        var zonesByProfessional = zoneRows
            .GroupBy(x => x.ProfessionalId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ZoneDto>)g
                    .Select(x => new ZoneDto(x.Id, x.Name ?? string.Empty))
                    .ToList());

        return professionals.Select(pf => new ProfessionalCardDto(
            pf.Id,
            pf.UserId,
            pf.Name,
            pf.AvatarUrl,
            pf.Rating,
            pf.Active,
            pf.CompletedJobsCount,
            pf.AvailabilityText,
            servicesByProfessional.GetValueOrDefault(pf.Id) ?? Array.Empty<ProfessionalServiceDto>(),
            zonesByProfessional.GetValueOrDefault(pf.Id) ?? Array.Empty<ZoneDto>())).ToList();
    }

    public async Task<IReadOnlyList<ZoneDto>> GetZonesAsync(CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = @"
            select z.""id"" as ""Id"", z.""name"" as ""Name""
            from ""Zone"" z
            where z.""active"" = true
            order by z.""name""";

        var rows = await conn.QueryAsync<ZoneDto>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync(CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = @"
            select s.""id"" as ""Id"", s.""name"" as ""Name"", s.""icon"" as ""Icon""
            from ""Service"" s
            order by s.""name""";

        var rows = await conn.QueryAsync<ServiceDto>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    private sealed record ProfessionalRow(
        string Id,
        string UserId,
        string Name,
        string? AvatarUrl,
        decimal? Rating,
        bool Active,
        int CompletedJobsCount,
        string? AvailabilityText);

    private sealed record ProfessionalServiceRow(
        string ProfessionalId,
        string Id,
        string ServiceId,
        string? Name,
        decimal Price,
        string? Description,
        string? Icon);

    private sealed record ProfessionalZoneRow(string ProfessionalId, string Id, string? Name);
}
