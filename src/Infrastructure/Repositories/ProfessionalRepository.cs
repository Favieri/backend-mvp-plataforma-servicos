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
            .Select(x => new ProfessionalCardDto
            {
                Id = x.p.Id,
                UserId = x.p.UserId,
                Name = x.u.Name,
                AvatarUrl = x.p.AvatarUrl,
                Rating = x.p.Rating,
                Active = x.p.Active,
                CompletedJobsCount = x.p.CompletedJobsCount,
                AvailabilityText = x.p.AvailabilityText
            })
            .ToListAsync(ct);

        if (professionals.Count == 0)
            return professionals;

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

        // Load zones for all professionals in one query
        var zones = await ctx.ProfessionalZones
            .AsNoTracking()
            .Where(pz => professionalIds.Contains(pz.ProfessionalId))
            .ToListAsync(ct);

        var servicesByProfessional = services
            .GroupBy(s => s.ProfessionalId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ProfessionalServiceDto>)g.Select(s => new ProfessionalServiceDto
                {
                    Id = s.Id,
                    ServiceId = s.ServiceId,
                    Name = s.Name ?? string.Empty,
                    Price = s.Price,
                    Description = s.Descricao
                }).ToList(),
                StringComparer.Ordinal);

        var zonesByProfessional = zones
            .GroupBy(z => z.ProfessionalId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(z => z.ZoneId).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        foreach (var p in professionals)
        {
            p.Services = servicesByProfessional.GetValueOrDefault(p.Id, []);
            p.Zones = zonesByProfessional.GetValueOrDefault(p.Id, []);
        }

        return professionals;
    }
}
