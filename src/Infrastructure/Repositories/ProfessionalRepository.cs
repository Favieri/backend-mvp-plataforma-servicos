using Application.Abstractions;
using Application.DTOs;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ProfessionalRepository(AppDbContext ctx) : IProfessionalRepository
{
    public async Task<IReadOnlyList<ProfessionalCardDto>> GetProfessionalCardsAsync(
        string? serviceId,
        string? zoneId,
        string? excludeProfessionalId,
        string? professionalId,
        bool filterZones,
        CancellationToken ct)
    {
        var query =
            from p in ctx.Professionals.AsNoTracking()
            join u in ctx.Users.AsNoTracking() on p.UserId equals u.Id
            where p.Active
            select new { p, u };

        if (!string.IsNullOrWhiteSpace(serviceId))
        {
            query = query.Where(x =>
                ctx.ProfessionalServices.Any(ps => ps.ProfessionalId == x.p.Id && ps.ServiceId == serviceId));
        }

        if (!string.IsNullOrWhiteSpace(zoneId))
        {
            query = query.Where(x =>
                ctx.ProfessionalZones.Any(pz => pz.ProfessionalId == x.p.Id && pz.ZoneId == zoneId));
        }

        if (!string.IsNullOrWhiteSpace(excludeProfessionalId))
        {
            query = query.Where(x => x.p.Id != excludeProfessionalId);
        }

        if (filterZones && !string.IsNullOrWhiteSpace(professionalId))
        {
            var currentZones = ctx.ProfessionalZones
                .Where(pz => pz.ProfessionalId == professionalId)
                .Select(pz => pz.ZoneId);

            query = query.Where(x =>
                ctx.ProfessionalZones.Any(pz =>
                    pz.ProfessionalId == x.p.Id && currentZones.Contains(pz.ZoneId)));
        }

        // ORDER BY active DESC, rating DESC NULLS LAST
        var professionals = await query
            .OrderByDescending(x => x.p.Active)
            .ThenByDescending(x => x.p.Rating.HasValue ? 1 : 0)
            .ThenByDescending(x => x.p.Rating)
            .Select(x => new
            {
                x.p.Id,
                x.p.UserId,
                Name = x.u.Name,
                x.p.AvatarUrl,
                x.p.Rating,
                x.p.Active,
                x.p.CompletedJobsCount,
                x.p.AvailabilityText,
                x.p.VerificationStatus,
                x.p.Badges,
                x.p.ResponseRate,
                x.p.AvgResponseTimeMinutes,
                x.p.CompletionRate
            })
            .ToListAsync(ct);

        if (professionals.Count == 0)
            return [];

        var professionalIds = professionals.Select(x => x.Id).Distinct().ToArray();

        // Load services for all professionals in one query
        var services = await ctx.ProfessionalServices
            .AsNoTracking()
            .Where(ps => professionalIds.Contains(ps.ProfessionalId))
            .Join(ctx.Services.AsNoTracking(), ps => ps.ServiceId, s => s.Id,
                (ps, s) => new
                {
                    ps.ProfessionalId,
                    ps.Id,
                    ps.ServiceId,
                    Name = string.IsNullOrEmpty(ps.NomeServico) ? s.Name : ps.NomeServico,
                    Price = ps.Preco,
                    ps.Descricao
                })
            .ToListAsync(ct);

        // Load zones for all professionals in one query (join to include zone name)
        var zoneRows = await (
            from pz in ctx.ProfessionalZones.AsNoTracking()
            join z in ctx.Zones.AsNoTracking() on pz.ZoneId equals z.Id
            where professionalIds.Contains(pz.ProfessionalId)
            select new { pz.ProfessionalId, ZoneId = pz.ZoneId, ZoneName = z.Name }
        ).ToListAsync(ct);

        // Load review counts for all professionals in one query
        var reviewCountsByProfessional = await ctx.Reviews
            .AsNoTracking()
            .Where(r => professionalIds.Contains(r.ProfessionalId))
            .GroupBy(r => r.ProfessionalId)
            .Select(g => new { ProfessionalId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ProfessionalId, g => g.Count, StringComparer.Ordinal, ct);

        var servicesByProfessional = services
            .GroupBy(s => s.ProfessionalId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ProfessionalServiceDto>)g.Select(s => new ProfessionalServiceDto
                {
                    Id = s.Id,
                    ServiceId = s.ServiceId,
                    Name = s.Name ?? string.Empty,
                    Price = s.Price
                }).ToList(),
                StringComparer.Ordinal);

        var zonesByProfessional = zoneRows
            .GroupBy(z => z.ProfessionalId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ZoneDto>)g.Select(z => new ZoneDto(z.ZoneId, z.ZoneName)).ToList(),
                StringComparer.Ordinal);

        return professionals.Select(p => new ProfessionalCardDto
        {
            Id = p.Id,
            UserId = p.UserId,
            Name = p.Name,
            AvatarUrl = p.AvatarUrl,
            Rating = p.Rating,
            ReviewCount = reviewCountsByProfessional.GetValueOrDefault(p.Id),
            Active = p.Active,
            CompletedJobsCount = p.CompletedJobsCount,
            AvailabilityText = p.AvailabilityText,
            VerificationStatus = p.VerificationStatus ?? "pending",
            Badges = p.Badges != null
                ? p.Badges.Split(',', StringSplitOptions.RemoveEmptyEntries)
                : [],
            ResponseRate = p.ResponseRate,
            AvgResponseTimeMinutes = p.AvgResponseTimeMinutes,
            CompletionRate = p.CompletionRate,
            Services = servicesByProfessional.GetValueOrDefault(p.Id, []),
            Zones = zonesByProfessional.GetValueOrDefault(p.Id, [])
        }).ToList();
    }
}
