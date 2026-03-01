using Application.Abstractions;
using Application.DTOs;
using Dapper;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class ProfessionalRepository(IConnectionFactory factory) : IProfessionalRepository
{
    public async Task<IReadOnlyList<ProfessionalCardDto>> GetProfessionalCardsAsync(
        string? serviceId,
        string? zoneId,
        string? excludeProfessionalId,
        string? professionalId,
        bool filterZones,
        CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);

        var where = " where p.\"active\" = true ";
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(serviceId))
        {
            where += " and exists (select 1 from \"ProfessionalService\" psf where psf.\"professionalId\" = p.\"id\" and psf.\"serviceId\" = @serviceId)";
            parameters.Add("serviceId", serviceId);
        }

        if (!string.IsNullOrWhiteSpace(zoneId))
        {
            where += " and exists (select 1 from \"ProfessionalZone\" pz where pz.\"professionalId\" = p.\"id\" and pz.\"zoneId\" = @zoneId)";
            parameters.Add("zoneId", zoneId);
        }

        if (!string.IsNullOrWhiteSpace(excludeProfessionalId))
        {
            where += " and p.\"id\" <> @excludeProfessionalId";
            parameters.Add("excludeProfessionalId", excludeProfessionalId);
        }

        if (filterZones && !string.IsNullOrWhiteSpace(professionalId))
        {
            where += " and exists (select 1 from \"ProfessionalZone\" pzTarget where pzTarget.\"professionalId\" = p.\"id\" and pzTarget.\"zoneId\" in (select pzCurrent.\"zoneId\" from \"ProfessionalZone\" pzCurrent where pzCurrent.\"professionalId\" = @professionalId))";
            parameters.Add("professionalId", professionalId);
        }

        const string baseSql = """
select
    p."id" as "Id",
    p."userId" as "UserId",
    u."name" as "Name",
    p."avatarUrl" as "AvatarUrl",
    p."rating" as "Rating",
    p."active" as "Active",
    p."completedJobsCount" as "CompletedJobsCount",
    p."availabilityText" as "AvailabilityText"
from "Professional" p
inner join "User" u on u."id" = p."userId"
""";

        var professionals = (await conn.QueryAsync<ProfessionalCardDto>(
            new CommandDefinition($"{baseSql}{where} order by p.\"active\" desc, p.\"rating\" desc nulls last", parameters, cancellationToken: ct)))
            .ToList();

        if (professionals.Count == 0)
        {
            return professionals;
        }

        var professionalIds = professionals.Select(x => x.Id).Distinct(StringComparer.Ordinal).ToArray();

        const string servicesSql = """
select
    ps."professionalId" as "ProfessionalId",
    ps."id" as "Id",
    ps."serviceId" as "ServiceId",
    coalesce(nullif(ps."nomeServico", ''), s."name", '') as "Name",
    ps."preco" as "Price",
    ps."descricao" as "Description"
from "ProfessionalService" ps
left join "Service" s on s."id" = ps."serviceId"
where ps."professionalId" = any(@professionalIds)
""";

        var services = await conn.QueryAsync<ProfessionalServiceRow>(
            new CommandDefinition(servicesSql, new { professionalIds }, cancellationToken: ct));

        var servicesByProfessionalId = services
            .GroupBy(x => x.ProfessionalId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ProfessionalServiceDto>)g.Select(s => new ProfessionalServiceDto
                {
                    Id = s.Id,
                    ServiceId = s.ServiceId,
                    Name = s.Name,
                    Price = s.Price,
                    Description = s.Description
                }).ToList(),
                StringComparer.Ordinal);

        const string zonesSql = """
select
    pz."professionalId" as "ProfessionalId",
    pz."zoneId" as "ZoneId"
from "ProfessionalZone" pz
where pz."professionalId" = any(@professionalIds)
""";

        var zones = await conn.QueryAsync<ProfessionalZoneRow>(
            new CommandDefinition(zonesSql, new { professionalIds }, cancellationToken: ct));

        var zonesByProfessionalId = zones
            .GroupBy(x => x.ProfessionalId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(z => z.ZoneId).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        foreach (var professional in professionals)
        {
            professional.Services = servicesByProfessionalId.GetValueOrDefault(professional.Id, []);
            professional.Zones = zonesByProfessionalId.GetValueOrDefault(professional.Id, []);
        }

        return professionals;
    }

    private sealed class ProfessionalServiceRow
    {
        public string ProfessionalId { get; init; } = string.Empty;
        public string Id { get; init; } = string.Empty;
        public string ServiceId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public double Price { get; init; }
        public string? Description { get; init; }
    }

    private sealed class ProfessionalZoneRow
    {
        public string ProfessionalId { get; init; } = string.Empty;
        public string ZoneId { get; init; } = string.Empty;
    }
}
