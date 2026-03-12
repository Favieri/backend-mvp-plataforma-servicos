namespace Domain.Entities;

/// <summary>
/// Represents a single execution of a RecurringPlan.
/// Each occurrence either produces an Order (order_created) or records a failure/skip.
/// </summary>
public class RecurringOccurrence
{
    public string Id { get; private set; } = default!;
    public string PlanId { get; private set; } = default!;

    /// <summary>FK to Order — null until the order is successfully created.</summary>
    public string? OrderId { get; private set; }

    /// <summary>Sequential number within the plan (1, 2, 3…).</summary>
    public int OccurrenceNumber { get; private set; }

    /// <summary>Date/time for which this occurrence was planned.</summary>
    public DateTime ScheduledFor { get; private set; }

    /// <summary>Status: pending | order_created | skipped | failed.</summary>
    public string Status { get; private set; } = default!;

    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Navigation
    public RecurringPlan? Plan { get; private set; }

    private RecurringOccurrence() { }

    public static RecurringOccurrence Create(
        string id,
        string planId,
        int occurrenceNumber,
        DateTime scheduledFor)
    {
        return new RecurringOccurrence
        {
            Id               = id,
            PlanId           = planId,
            OccurrenceNumber = occurrenceNumber,
            ScheduledFor     = scheduledFor,
            Status           = Enums.RecurringOccurrenceStatus.Pending,
            CreatedAt        = DateTime.UtcNow
        };
    }

    // ─── State mutations ────────────────────────────────────────────────────

    public void MarkOrderCreated(string orderId)
    {
        OrderId = orderId;
        Status  = Enums.RecurringOccurrenceStatus.OrderCreated;
    }

    public void MarkFailed(string reason)
    {
        Status        = Enums.RecurringOccurrenceStatus.Failed;
        FailureReason = reason;
    }

    public void MarkSkipped(string reason)
    {
        Status        = Enums.RecurringOccurrenceStatus.Skipped;
        FailureReason = reason;
    }
}
