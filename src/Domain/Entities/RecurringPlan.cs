using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Represents a recurring service contract created from a completed order.
/// The RecurringBillingJob checks next_billing_at daily and generates a new Order per occurrence.
/// </summary>
public class RecurringPlan
{
    public string Id { get; private set; } = default!;
    public string ClientId { get; private set; } = default!;
    public string ProfessionalId { get; private set; } = default!;
    public string ServiceId { get; private set; } = default!;

    /// <summary>The completed order that originated this plan (rebook source).</summary>
    public string SourceOrderId { get; private set; } = default!;

    /// <summary>Billing frequency: weekly | biweekly | monthly.</summary>
    public string Frequency { get; private set; } = default!;

    /// <summary>Base price in cents (before discount).</summary>
    public int PriceTotalCents { get; private set; }

    /// <summary>Recurring discount percentage (0–100). e.g. 10 = 10% off.</summary>
    public int DiscountPercent { get; private set; }

    /// <summary>Effective price after discount, computed in cents.</summary>
    public int EffectivePriceCents => PriceTotalCents - (PriceTotalCents * DiscountPercent / 100);

    public string? PaymentMethod { get; private set; }
    public string? Scope { get; private set; }
    public string? AddressId { get; private set; }

    // ─── Service address snapshot ────────────────────────────────────────────
    public string? SvcAddrZipCode { get; private set; }
    public string? SvcAddrStreet { get; private set; }
    public string? SvcAddrNumber { get; private set; }
    public string? SvcAddrNeighborhood { get; private set; }
    public string? SvcAddrCity { get; private set; }
    public string? SvcAddrState { get; private set; }
    public string? SvcAddrComplement { get; private set; }
    public string? SvcAddrReference { get; private set; }

    /// <summary>Plan lifecycle: active | paused | cancelled.</summary>
    public string Status { get; private set; } = default!;

    /// <summary>Next date the RecurringBillingJob should generate an Order.</summary>
    public DateTime NextBillingAt { get; private set; }

    /// <summary>How many occurrences have been successfully generated.</summary>
    public int OccurrenceCount { get; private set; }

    public DateTime StartedAt { get; private set; }
    public DateTime? PausedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Navigation
    public ICollection<RecurringOccurrence> Occurrences { get; private set; } = [];

    private RecurringPlan() { }

    /// <summary>Returns the service address snapshot as an AddressData, or null if not set.</summary>
    public AddressData? GetServiceAddress()
    {
        if (string.IsNullOrWhiteSpace(SvcAddrZipCode)) return null;
        return new AddressData(
            SvcAddrZipCode!, SvcAddrStreet!, SvcAddrNumber!,
            SvcAddrNeighborhood!, SvcAddrCity!, SvcAddrState!,
            SvcAddrComplement, SvcAddrReference);
    }

    /// <summary>
    /// Creates a new recurring plan from a completed (or rebooked) order.
    /// </summary>
    public static RecurringPlan Create(
        string id,
        string clientId,
        string professionalId,
        string serviceId,
        string sourceOrderId,
        string frequency,
        int priceTotalCents,
        int discountPercent,
        string? paymentMethod,
        string? scope,
        string? addressId,
        AddressData? serviceAddress = null)
    {
        var now = DateTime.UtcNow;
        var intervalDays = Enums.RecurringFrequency.ToDays(frequency);

        return new RecurringPlan
        {
            Id              = id,
            ClientId        = clientId,
            ProfessionalId  = professionalId,
            ServiceId       = serviceId,
            SourceOrderId   = sourceOrderId,
            Frequency       = frequency,
            PriceTotalCents = priceTotalCents,
            DiscountPercent = discountPercent,
            PaymentMethod   = paymentMethod,
            Scope           = scope,
            AddressId       = addressId,
            SvcAddrZipCode      = serviceAddress?.ZipCode,
            SvcAddrStreet       = serviceAddress?.Street,
            SvcAddrNumber       = serviceAddress?.Number,
            SvcAddrNeighborhood = serviceAddress?.Neighborhood,
            SvcAddrCity         = serviceAddress?.City,
            SvcAddrState        = serviceAddress?.State,
            SvcAddrComplement   = serviceAddress?.Complement,
            SvcAddrReference    = serviceAddress?.Reference,
            Status          = Enums.RecurringPlanStatus.Active,
            NextBillingAt   = now.AddDays(intervalDays),
            OccurrenceCount = 0,
            StartedAt       = now,
            CreatedAt       = now
        };
    }

    // ─── State mutations ────────────────────────────────────────────────────

    /// <summary>Advances NextBillingAt after a successful occurrence is generated.</summary>
    public void AdvanceBilling()
    {
        var intervalDays = Enums.RecurringFrequency.ToDays(Frequency);
        NextBillingAt = NextBillingAt.AddDays(intervalDays);
        OccurrenceCount++;
    }

    public void Pause()
    {
        Status   = Enums.RecurringPlanStatus.Paused;
        PausedAt = DateTime.UtcNow;
    }

    public void Resume()
    {
        Status   = Enums.RecurringPlanStatus.Active;
        PausedAt = null;
    }

    public void Cancel()
    {
        Status      = Enums.RecurringPlanStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
    }
}
