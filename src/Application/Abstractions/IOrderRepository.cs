using Domain.Entities;

namespace Application.Abstractions;

public interface IOrderRepository
{
    // ─── Legacy ──────────────────────────────────────────────────────────────
    Task<IReadOnlyList<Order>> GetOrdersAsync(string? serviceId, string? excludeProfessionalId, string? professionalId, bool filterZones, CancellationToken ct);
    Task<Order?> GetByIdAsync(string id, CancellationToken ct);
    Task<Order> CreateAsync(string clientId, string serviceId, string? description, string? location, DateTime? date, CancellationToken ct);
    Task CompleteOrderAsync(string orderId, CancellationToken ct);
    Task<IReadOnlyList<object>> GetMineAsync(string clientId, CancellationToken ct);

    // ─── Phase 1 ─────────────────────────────────────────────────────────────
    Task<Order> CreateBookingAsync(Order order, CancellationToken ct);
    Task<Order> CreateFromProposalAsync(Order order, CancellationToken ct);
    Task<bool> UpdateStatusAsync(string orderId, string newStatus, CancellationToken ct);
    Task<bool> MarkAwaitingConfirmationAsync(string orderId, int autoConfirmHours, CancellationToken ct);
    Task<bool> MarkCompletedAsync(string orderId, CancellationToken ct);
    Task<bool> MarkCancelledByClientAsync(string orderId, string? reason, CancellationToken ct);
    Task<bool> MarkCancelledByProfessionalAsync(string orderId, string? reason, CancellationToken ct);
    Task<bool> MarkDisputedAsync(string orderId, CancellationToken ct);
    Task<IReadOnlyList<object>> GetMineByRoleAsync(string userId, string role, CancellationToken ct);
    Task<IReadOnlyList<Order>> GetOrdersAwaitingAutoConfirmAsync(DateTime before, CancellationToken ct);
    Task<IReadOnlyList<Order>> GetOrdersAwaitingPaymentTimedOutAsync(DateTime before, CancellationToken ct);
}
