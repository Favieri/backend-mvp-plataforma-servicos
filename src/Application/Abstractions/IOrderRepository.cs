using Application.DTOs;
using Domain.Entities;

namespace Application.Abstractions;

public interface IOrderRepository
{
    // ─── Legacy ──────────────────────────────────────────────────────────────
    Task<PagedResult<Order>> GetOrdersAsync(string? serviceId, string? excludeProfessionalId, string? professionalId, bool filterZones, bool active, int page, int pageSize, CancellationToken ct);
    Task<Order?> GetByIdAsync(string id, CancellationToken ct);
    Task<Order> CreateAsync(string clientId, string serviceId, string? description, string? location, DateTime? date, CancellationToken ct, int? maxProposals = null);
    Task CompleteOrderAsync(string orderId, CancellationToken ct);
    Task<PagedResult<object>> GetMineAsync(string clientId, int page, int pageSize, CancellationToken ct);

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
    Task<bool> MarkRefundedAsync(string orderId, CancellationToken ct);

    // ─── Lead flow: limite do cliente + priorização por reputação ────────────
    /// <summary>Fecha o lead imediatamente — cliente aceitou uma proposta, independentemente do limite.</summary>
    Task<bool> MarkConvertidoAsync(string orderId, CancellationToken ct);
    /// <summary>Atingiu o limite de propostas do cliente — só transiciona a partir de 'aberto'.</summary>
    Task<bool> MarkPropostasCompletasAsync(string orderId, CancellationToken ct);
    /// <summary>Reabre o lead — só transiciona a partir de 'propostas_completas'.</summary>
    Task<bool> ReopenAbertoAsync(string orderId, CancellationToken ct);
}
