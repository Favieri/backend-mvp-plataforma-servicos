using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class RecurringPlanRepository(AppDbContext ctx) : IRecurringPlanRepository
{
    // ─── Queries ─────────────────────────────────────────────────────────────

    public async Task<RecurringPlan?> GetByIdAsync(string id, CancellationToken ct = default)
        => await ctx.RecurringPlans
            .Include(p => p.Occurrences)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<RecurringPlan>> GetDueForBillingAsync(DateTime asOf, CancellationToken ct = default)
        => await ctx.RecurringPlans
            .Where(p => p.Status == Domain.Enums.RecurringPlanStatus.Active && p.NextBillingAt <= asOf)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RecurringPlan>> GetByClientIdAsync(string clientId, CancellationToken ct = default)
        => await ctx.RecurringPlans
            .Where(p => p.ClientId == clientId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RecurringPlan>> GetByProfessionalIdAsync(string professionalId, CancellationToken ct = default)
        => await ctx.RecurringPlans
            .Where(p => p.ProfessionalId == professionalId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    // ─── Mutations ────────────────────────────────────────────────────────────

    public async Task<RecurringPlan> CreateAsync(RecurringPlan plan, CancellationToken ct = default)
    {
        ctx.RecurringPlans.Add(plan);
        await ctx.SaveChangesAsync(ct);
        return plan;
    }

    public async Task AdvanceBillingAsync(string planId, CancellationToken ct = default)
    {
        var plan = await ctx.RecurringPlans.FindAsync([planId], ct);
        if (plan is null) return;

        plan.AdvanceBilling();
        await ctx.SaveChangesAsync(ct);
    }

    public async Task PauseAsync(string planId, CancellationToken ct = default)
    {
        var plan = await ctx.RecurringPlans.FindAsync([planId], ct);
        if (plan is null) return;

        plan.Pause();
        await ctx.SaveChangesAsync(ct);
    }

    public async Task ResumeAsync(string planId, CancellationToken ct = default)
    {
        var plan = await ctx.RecurringPlans.FindAsync([planId], ct);
        if (plan is null) return;

        plan.Resume();
        await ctx.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(string planId, CancellationToken ct = default)
    {
        var plan = await ctx.RecurringPlans.FindAsync([planId], ct);
        if (plan is null) return;

        plan.Cancel();
        await ctx.SaveChangesAsync(ct);
    }

    // ─── Occurrences ─────────────────────────────────────────────────────────

    public async Task<RecurringOccurrence> AddOccurrenceAsync(RecurringOccurrence occurrence, CancellationToken ct = default)
    {
        ctx.RecurringOccurrences.Add(occurrence);
        await ctx.SaveChangesAsync(ct);
        return occurrence;
    }

    public async Task<IReadOnlyList<RecurringOccurrence>> GetOccurrencesByPlanIdAsync(string planId, CancellationToken ct = default)
        => await ctx.RecurringOccurrences
            .Where(o => o.PlanId == planId)
            .OrderBy(o => o.OccurrenceNumber)
            .ToListAsync(ct);

    public async Task MarkOccurrenceOrderCreatedAsync(string occurrenceId, string orderId, CancellationToken ct = default)
    {
        var occ = await ctx.RecurringOccurrences.FindAsync([occurrenceId], ct);
        if (occ is null) return;

        occ.MarkOrderCreated(orderId);
        await ctx.SaveChangesAsync(ct);
    }

    public async Task MarkOccurrenceFailedAsync(string occurrenceId, string reason, CancellationToken ct = default)
    {
        var occ = await ctx.RecurringOccurrences.FindAsync([occurrenceId], ct);
        if (occ is null) return;

        occ.MarkFailed(reason);
        await ctx.SaveChangesAsync(ct);
    }

    public async Task MarkOccurrenceSkippedAsync(string occurrenceId, string reason, CancellationToken ct = default)
    {
        var occ = await ctx.RecurringOccurrences.FindAsync([occurrenceId], ct);
        if (occ is null) return;

        occ.MarkSkipped(reason);
        await ctx.SaveChangesAsync(ct);
    }
}
