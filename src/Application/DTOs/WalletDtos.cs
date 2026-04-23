namespace Application.DTOs;

public record WalletBalance(
    string ProfessionalId,
    int PendingCents,
    int AvailableCents,
    int DisputedCents,
    int TotalEarnedCents,
    string Currency,
    bool MpConnected,
    DateTime? LastUpdatedAt
);

public record LedgerEntryDetail(
    Guid Id,
    string Type,
    string TypeLabel,
    int AmountCents,
    string Sign,
    string? OrderId,
    string? OrderStatus,
    string? ServiceName,
    string? ClientName,
    DateTime CreatedAt
);

public record MonthlySummary(string Month, int EarnedCents, int ServicesCount);
