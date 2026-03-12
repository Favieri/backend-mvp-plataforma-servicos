using Domain.Entities;

namespace Application.Abstractions;

public interface IRecurringPlanRepository
{
    // ─── Queries ─────────────────────────────────────────────────────────────

    Task<RecurringPlan?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Returns all active plans where next_billing_at &lt;= now (used by RecurringBillingJob).</summary>
    Task<IReadOnlyList<RecurringPlan>> GetDueForBillingAsync(DateTime asOf, CancellationToken ct = default);

    /// <summary>Returns all plans belonging to a client.</summary>
    Task<IReadOnlyList<RecurringPlan>> GetByClientIdAsync(string clientId, CancellationToken ct = default);

    /// <summary>Returns all plans assigned to a professional.</summary>
    Task<IReadOnlyList<RecurringPlan>> GetByProfessionalIdAsync(string professionalId, CancellationToken ct = default);

    // ─── Mutations ────────────────────────────────────────────────────────────

    Task<RecurringPlan> CreateAsync(RecurringPlan plan, CancellationToken ct = default);

    /// <summary>Advances NextBillingAt and increments OccurrenceCount after a successful billing tick.</summary>
    Task AdvanceBillingAsync(string planId, CancellationToken ct = default);

    Task PauseAsync(string planId, CancellationToken ct = default);
    Task ResumeAsync(string planId, CancellationToken ct = default);
    Task CancelAsync(string planId, CancellationToken ct = default);

    // ─── Occurrences ─────────────────────────────────────────────────────────

    Task<RecurringOccurrence> AddOccurrenceAsync(RecurringOccurrence occurrence, CancellationToken ct = default);

    Task<IReadOnlyList<RecurringOccurrence>> GetOccurrencesByPlanIdAsync(string planId, CancellationToken ct = default);

    Task MarkOccurrenceOrderCreatedAsync(string occurrenceId, string orderId, CancellationToken ct = default);
    Task MarkOccurrenceFailedAsync(string occurrenceId, string reason, CancellationToken ct = default);
    Task MarkOccurrenceSkippedAsync(string occurrenceId, string reason, CancellationToken ct = default);
}
