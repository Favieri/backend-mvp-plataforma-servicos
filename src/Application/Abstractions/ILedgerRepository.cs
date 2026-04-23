using Application.DTOs;
using Domain.Entities;

namespace Application.Abstractions;

public interface ILedgerRepository
{
    Task AddAsync(LedgerEntry entry, CancellationToken ct);
    Task<WalletBalance> GetBalanceAsync(string professionalId, CancellationToken ct);
    Task<(IReadOnlyList<LedgerEntryDetail> Items, int Total)> GetLedgerAsync(
        string professionalId, int page, int pageSize,
        DateTime? from, DateTime? to, string? type, CancellationToken ct);
    Task<IReadOnlyList<MonthlySummary>> GetMonthlySummaryAsync(
        string professionalId, int months, CancellationToken ct);
}
