using Domain.Entities;

namespace Application.Abstractions;

public interface IOrderTimelineRepository
{
    Task AddEventAsync(OrderTimeline timeline, CancellationToken ct);
    Task<IReadOnlyList<OrderTimeline>> GetByOrderIdAsync(string orderId, CancellationToken ct);
}
