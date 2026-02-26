namespace Domain.Entities;

public sealed record Payment(
    string Id,
    string OrderId,
    string Gateway,
    string GatewayRef,
    string Method,
    int AmountCents,
    string Status,
    DateTime CreatedAt,
    DateTime? PaidAt);
