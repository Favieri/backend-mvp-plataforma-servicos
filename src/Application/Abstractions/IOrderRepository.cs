using Domain.Entities;

namespace Application.Abstractions;

public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> GetOrdersAsync(string? serviceId, string? excludeProfessionalId, string? professionalId, bool filterZones, CancellationToken ct);
    Task<Order?> GetByIdAsync(string id, CancellationToken ct);
    Task<Order> CreateAsync(string clientId, string serviceId, string? description, string? location, DateTime? date, CancellationToken ct);
    Task CompleteOrderAsync(string orderId, CancellationToken ct);
    Task<IReadOnlyList<object>> GetMineAsync(string clientId, CancellationToken ct);
}
