using Application.Abstractions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ProfessionalDetailRepository(AppDbContext ctx) : IProfessionalDetailRepository
{
    public async Task<object?> GetByIdAsync(string id, CancellationToken ct)
    {
        var row = await (
            from p in ctx.Professionals.AsNoTracking()
            join u in ctx.Users.AsNoTracking() on p.UserId equals u.Id
            where p.Id == id
            select new
            {
                p.Id, p.UserId, p.Bio, p.Rating, p.Active, p.AvatarUrl, p.AvailabilityText,
                p.CompletedJobsCount, p.SlotMinutes, p.LeadTimeMinutes, p.MaxAdvanceDays, p.AllowInstantBooking,
                UserId2 = u.Id, UserName = u.Name, UserEmail = u.Email, UserPhone = u.Phone,
                UserRole = u.Role, UserZoneId = u.ZoneId, UserCreatedAt = u.CreatedAt
            }
        ).FirstOrDefaultAsync(ct);

        if (row is null) return null;

        var services = await ctx.ProfessionalServices
            .AsNoTracking()
            .Where(ps => ps.ProfessionalId == id)
            .Select(ps => new
            {
                id = ps.Id,
                serviceId = ps.ServiceId,
                professionalId = ps.ProfessionalId,
                nomeServico = ps.NomeServico,
                preco = ps.Preco,
                descricao = ps.Descricao
            })
            .ToListAsync(ct);

        var portfolio = await ctx.ProfessionalPortfolios
            .AsNoTracking()
            .Where(p => p.ProfessionalId == id)
            .OrderBy(p => p.OrderIndex == null ? 1 : 0)
            .ThenBy(p => p.OrderIndex)
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                id = p.Id,
                professionalId = p.ProfessionalId,
                imageUrl = p.ImageUrl,
                title = p.Title,
                description = p.Description,
                orderIndex = p.OrderIndex,
                createdAt = p.CreatedAt
            })
            .ToListAsync(ct);

        var zones = await (
            from pz in ctx.ProfessionalZones.AsNoTracking()
            join z in ctx.Zones.AsNoTracking() on pz.ZoneId equals z.Id
            where pz.ProfessionalId == id
            select new
            {
                professionalId = pz.ProfessionalId,
                zoneId = pz.ZoneId,
                zone = new { id = z.Id, name = z.Name, active = z.Active }
            }
        ).ToListAsync(ct);

        return new
        {
            id = row.Id,
            userId = row.UserId,
            bio = row.Bio,
            rating = row.Rating,
            active = row.Active,
            avatarUrl = row.AvatarUrl,
            availabilityText = row.AvailabilityText,
            completedJobsCount = row.CompletedJobsCount,
            slotMinutes = row.SlotMinutes,
            leadTimeMinutes = row.LeadTimeMinutes,
            maxAdvanceDays = row.MaxAdvanceDays,
            allowInstantBooking = row.AllowInstantBooking,
            user = new { id = row.UserId2, name = row.UserName, email = row.UserEmail, phone = row.UserPhone, role = row.UserRole, zoneId = row.UserZoneId, createdAt = row.UserCreatedAt },
            services,
            portfolio,
            zones
        };
    }

    public async Task<object?> UpdateAsync(
        string id, string? bio, bool? active, string? availabilityText, string? avatarUrl, CancellationToken ct)
    {
        var existing = await ctx.Professionals
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (existing is null) return null;

        var newBio = bio ?? existing.Bio;
        var newActive = active ?? existing.Active;
        var newAvailabilityText = availabilityText ?? existing.AvailabilityText;
        var newAvatarUrl = avatarUrl ?? existing.AvatarUrl;

        await ctx.Professionals
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Bio, newBio)
                .SetProperty(p => p.Active, newActive)
                .SetProperty(p => p.AvailabilityText, newAvailabilityText)
                .SetProperty(p => p.AvatarUrl, newAvatarUrl), ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<IReadOnlyList<object>> GetZonesAsync(string professionalId, CancellationToken ct)
    {
        var rows = await (
            from pz in ctx.ProfessionalZones.AsNoTracking()
            join z in ctx.Zones.AsNoTracking() on pz.ZoneId equals z.Id
            where pz.ProfessionalId == professionalId
            select new
            {
                professionalId = pz.ProfessionalId,
                zoneId = pz.ZoneId,
                zone = new { id = z.Id, name = z.Name, active = z.Active }
            }
        ).ToListAsync(ct);

        return rows.Cast<object>().ToList();
    }

    public async Task<object?> UpdateZonesAsync(string professionalId, string[] zoneIds, CancellationToken ct)
    {
        // Validate zones exist and are active
        if (zoneIds.Length > 0)
        {
            var found = await ctx.Zones
                .AsNoTracking()
                .Where(z => zoneIds.Contains(z.Id) && z.Active)
                .Select(z => z.Id)
                .ToHashSetAsync(ct);

            var invalid = zoneIds.Where(z => !found.Contains(z)).ToList();
            if (invalid.Count > 0)
                throw new InvalidOperationException($"Zonas inválidas/inativas: {string.Join(", ", invalid)}");
        }

        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        await ctx.ProfessionalZones
            .Where(pz => pz.ProfessionalId == professionalId)
            .ExecuteDeleteAsync(ct);

        if (zoneIds.Length > 0)
        {
            var entities = zoneIds.Select(zId => new Domain.Entities.ProfessionalZone(
                ProfessionalId: professionalId,
                ZoneId: zId,
                CreatedAt: DateTime.UtcNow)).ToList();

            await ctx.ProfessionalZones.AddRangeAsync(entities, ct);
            await ctx.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        return await GetByIdAsync(professionalId, ct);
    }
}
