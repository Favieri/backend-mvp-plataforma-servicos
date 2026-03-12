using Application.Abstractions;
using Application.DTOs;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ProfessionalReadRepository(AppDbContext ctx) : IProfessionalReadRepository
{
    public Task<IReadOnlyList<ProfessionalCardDto>> GetProfessionalsAsync(
        string? zoneId, string? serviceId, CancellationToken ct)
        => GetProfessionalsFilteredAsync(zoneId, serviceId, null, null, ct);

    public async Task<IReadOnlyList<ProfessionalCardDto>> GetProfessionalsFilteredAsync(
        string? zoneId,
        string? serviceId,
        string? verificationStatus,
        double? minRating,
        CancellationToken ct)
    {
        var query =
            from p in ctx.Professionals.AsNoTracking()
            join u in ctx.Users.AsNoTracking() on p.UserId equals u.Id
            where p.Active
            select new { p, u };

        if (!string.IsNullOrWhiteSpace(zoneId))
        {
            query = query.Where(x =>
                ctx.ProfessionalZones.Any(pz => pz.ProfessionalId == x.p.Id && pz.ZoneId == zoneId));
        }

        if (!string.IsNullOrWhiteSpace(serviceId))
        {
            query = query.Where(x =>
                ctx.ProfessionalServices.Any(ps => ps.ProfessionalId == x.p.Id && ps.ServiceId == serviceId));
        }

        if (!string.IsNullOrWhiteSpace(verificationStatus))
        {
            query = query.Where(x => x.p.VerificationStatus == verificationStatus);
        }

        if (minRating.HasValue)
        {
            query = query.Where(x => x.p.Rating.HasValue && x.p.Rating >= minRating.Value);
        }

        var professionals = await query
            .OrderBy(x => x.p.Id)
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

        var serviceRows = await ctx.ProfessionalServices
            .AsNoTracking()
            .Where(ps => professionalIds.Contains(ps.ProfessionalId))
            .Join(ctx.Services.AsNoTracking(), ps => ps.ServiceId, s => s.Id,
                (ps, s) => new
                {
                    ps.ProfessionalId,
                    ps.Id,
                    ps.ServiceId,
                    Name = ps.NomeServico ?? string.Empty,
                    Price = ps.Preco,
                    Description = ps.Descricao,
                    Icon = s.Icon
                })
            .ToListAsync(ct);

        var zoneRows = await (
            from pz in ctx.ProfessionalZones.AsNoTracking()
            join z in ctx.Zones.AsNoTracking() on pz.ZoneId equals z.Id
            where professionalIds.Contains(pz.ProfessionalId) && z.Active
            select new { pz.ProfessionalId, ZoneName = z.Name }
        ).ToListAsync(ct);

        var servicesByProfessional = serviceRows
            .GroupBy(x => x.ProfessionalId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ProfessionalServiceDto>)g.Select(x => new ProfessionalServiceDto
                {
                    Id = x.Id,
                    ServiceId = x.ServiceId,
                    Name = x.Name,
                    Price = x.Price,
                    Description = x.Description
                }).ToList(),
                StringComparer.Ordinal);

        var zonesByProfessional = zoneRows
            .GroupBy(x => x.ProfessionalId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => x.ZoneName).ToList(),
                StringComparer.Ordinal);

        return professionals.Select(p => new ProfessionalCardDto
        {
            Id = p.Id,
            UserId = p.UserId,
            Name = p.Name,
            AvatarUrl = p.AvatarUrl,
            Rating = p.Rating,
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
            Services = servicesByProfessional.GetValueOrDefault(p.Id) ?? [],
            Zones = zonesByProfessional.GetValueOrDefault(p.Id) ?? []
        }).ToList();
    }

    public async Task<IReadOnlyList<ZoneDto>> GetZonesAsync(CancellationToken ct)
        => await ctx.Zones
            .AsNoTracking()
            .Where(z => z.Active)
            .OrderBy(z => z.Name)
            .Select(z => new ZoneDto(z.Id, z.Name))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync(CancellationToken ct)
        => await ctx.Services
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new ServiceDto(s.Id, s.Name, s.Icon, s.CategoryId, s.TierId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ServiceDto>> GetServicesByCategoryAsync(string categoryId, CancellationToken ct)
        => await ctx.Services
            .AsNoTracking()
            .Where(s => s.CategoryId == categoryId)
            .OrderBy(s => s.Name)
            .Select(s => new ServiceDto(s.Id, s.Name, s.Icon, s.CategoryId, s.TierId))
            .ToListAsync(ct);
}
