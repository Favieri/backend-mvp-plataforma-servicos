using Domain.Entities;

namespace Application.Abstractions;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(string paymentId, CancellationToken ct);
    Task<Payment?> GetByOrderIdAsync(string orderId, CancellationToken ct);
    Task<Payment?> GetPendingByOrderIdAsync(string orderId, CancellationToken ct);
    Task<Payment?> GetPaidByOrderIdAsync(string orderId, CancellationToken ct);
    Task CreateAsync(Payment payment, CancellationToken ct);
    Task UpdateStatusAsync(string paymentId, string status, CancellationToken ct);
    Task CancelPendingByOrderIdAsync(string orderId, CancellationToken ct);
    Task MarkRefundPendingAsync(string paymentId, string reason, CancellationToken ct);
    Task MarkRefundedAsync(string paymentId, string refundId, string reason, CancellationToken ct);
    Task MarkRefundFailedAsync(string paymentId, CancellationToken ct);
    Task<IReadOnlyList<Payment>> GetRefundPendingAsync(int maxCount, CancellationToken ct);
}
