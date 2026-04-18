using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AvailabilityRepository(AppDbContext ctx) : IAvailabilityRepository
{
    public async Task<IReadOnlyList<object>> GetByProfessionalAsync(string professionalId, CancellationToken ct)
    {
        var rows = await ctx.ProfessionalAvailabilities
            .AsNoTracking()
            .Where(a => a.ProfessionalId == professionalId)
            .OrderBy(a => a.Weekday)
            .ThenBy(a => a.StartMinutes)
            .Select(a => new
            {
                id = a.Id,
                professionalId = a.ProfessionalId,
                weekday = a.Weekday,
                startMinutes = a.StartMinutes,
                endMinutes = a.EndMinutes,
                active = a.Active
            })
            .ToListAsync(ct);

        return rows.Cast<object>().ToList();
    }

    public async Task SaveAllAsync(
    string professionalId,
    IReadOnlyList<(int Weekday, int StartMinutes, int EndMinutes, bool Active)> rows,
    CancellationToken ct)
    {
        var strategy = ctx.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);

            await ctx.ProfessionalAvailabilities
                .Where(a => a.ProfessionalId == professionalId)
                .ExecuteDeleteAsync(ct);

            var entities = rows
                .Select(r => new ProfessionalAvailability(
                    Id: Guid.NewGuid().ToString(),
                    ProfessionalId: professionalId,
                    Weekday: r.Weekday % 7,
                    StartMinutes: r.StartMinutes,
                    EndMinutes: r.EndMinutes,
                    Active: r.Active))
                .ToList();

            if (entities.Count > 0)
            {
                await ctx.ProfessionalAvailabilities.AddRangeAsync(entities, ct);
                await ctx.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        });
    }

    public async Task<IReadOnlyList<object>> GetBlocksAsync(
        string professionalId, DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = await ctx.ProfessionalBlocks
            .AsNoTracking()
            .Where(b => b.ProfessionalId == professionalId && b.StartsAt >= from && b.EndsAt <= to)
            .OrderBy(b => b.StartsAt)
            .Select(b => new
            {
                id = b.Id,
                professionalId = b.ProfessionalId,
                startsAt = b.StartsAt,
                endsAt = b.EndsAt,
                reason = b.Reason,
                createdAt = b.CreatedAt
            })
            .ToListAsync(ct);

        return rows.Cast<object>().ToList();
    }

    public async Task<object> CreateBlockAsync(
        string professionalId, DateTime startsAt, DateTime endsAt, string? reason, CancellationToken ct)
    {
        var block = new ProfessionalBlock(
            Id: Guid.NewGuid().ToString(),
            ProfessionalId: professionalId,
            StartsAt: startsAt,
            EndsAt: endsAt,
            Reason: reason,
            CreatedAt: DateTime.UtcNow);

        ctx.ProfessionalBlocks.Add(block);
        await ctx.SaveChangesAsync(ct);

        return new
        {
            id = block.Id,
            professionalId = block.ProfessionalId,
            startsAt = block.StartsAt,
            endsAt = block.EndsAt,
            reason = block.Reason,
            createdAt = block.CreatedAt
        };
    }

    public async Task<bool> ProfessionalExistsAsync(string professionalId, CancellationToken ct)
        => await ctx.Professionals.AsNoTracking().AnyAsync(p => p.Id == professionalId, ct);

    public async Task<object?> GetProfessionalSchedulingConfigAsync(string professionalId, CancellationToken ct)
        => await ctx.Professionals
            .AsNoTracking()
            .Where(p => p.Id == professionalId)
            .Select(p => new
            {
                id = p.Id,
                slotMinutes = p.SlotMinutes,
                leadTimeMinutes = p.LeadTimeMinutes,
                maxAdvanceDays = p.MaxAdvanceDays,
                allowInstantBooking = p.AllowInstantBooking
            })
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ProfessionalAvailability>> GetAvailabilityForDayAsync(
        string professionalId, int weekday, CancellationToken ct)
        => await ctx.ProfessionalAvailabilities
            .AsNoTracking()
            .Where(a => a.ProfessionalId == professionalId && a.Active && a.Weekday == weekday)
            .OrderBy(a => a.StartMinutes)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Appointment>> GetAppointmentsForDayAsync(
        string professionalId, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken ct)
        => await ctx.Appointments
            .AsNoTracking()
            .Where(a =>
                a.ProfessionalId == professionalId
                && (a.Status == "PENDING" || a.Status == "CONFIRMED")
                && a.StartsAt < dayEndUtc
                && a.EndsAt > dayStartUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProfessionalBlock>> GetBlocksForDayAsync(
        string professionalId, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken ct)
        => await ctx.ProfessionalBlocks
            .AsNoTracking()
            .Where(b =>
                b.ProfessionalId == professionalId
                && b.StartsAt < dayEndUtc
                && b.EndsAt > dayStartUtc)
            .ToListAsync(ct);

    public async Task<int?> GetProfessionalServiceDurationAsync(string professionalServiceId, CancellationToken ct)
        => await ctx.ProfessionalServices
            .AsNoTracking()
            .Where(ps => ps.Id == professionalServiceId)
            .Select(ps => ps.DurationMinutes)
            .FirstOrDefaultAsync(ct);
}
