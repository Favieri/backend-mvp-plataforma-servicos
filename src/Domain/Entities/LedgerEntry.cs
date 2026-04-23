namespace Domain.Entities;

public sealed class LedgerEntry
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = default!;
    public string? OrderId { get; private set; }
    public string? PaymentId { get; private set; }
    public string? ProfessionalId { get; private set; }
    public int AmountCents { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private LedgerEntry() { }

    public static LedgerEntry Create(
        string type,
        string? orderId,
        string? paymentId,
        string? professionalId,
        int amountCents) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        OrderId = orderId,
        PaymentId = paymentId,
        ProfessionalId = professionalId,
        AmountCents = amountCents,
        CreatedAt = DateTime.UtcNow
    };
}
