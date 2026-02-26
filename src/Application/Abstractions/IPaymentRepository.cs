using Domain.Entities;

namespace Application.Abstractions;

public interface IPaymentRepository
{
    Task<Payment> UpsertAsync(Payment payment, CancellationToken ct);
    Task<Payment?> GetLatestByOrderAsync(string orderId, CancellationToken ct);
    Task<bool> TryStartWebhookProcessingAsync(string provider, string externalEventId, string rawPayload, CancellationToken ct);
    Task MarkWebhookProcessedAsync(string provider, string externalEventId, CancellationToken ct);
    Task ApplyPaymentStatusAsync(string gatewayRef, string status, DateTime? paidAt, CancellationToken ct);
    Task<int> GetWalletBalanceAsync(string professionalId, CancellationToken ct);
    Task<IReadOnlyList<object>> GetLedgerAsync(string professionalId, CancellationToken ct);
}
