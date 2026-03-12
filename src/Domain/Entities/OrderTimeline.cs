namespace Domain.Entities;

public class OrderTimeline
{
    public string Id { get; private set; } = default!;
    public string OrderId { get; private set; } = default!;
    public string EventType { get; private set; } = default!;
    public string? ActorId { get; private set; }
    public string? ActorRole { get; private set; }
    public string? Metadata { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private OrderTimeline() { }

    public static OrderTimeline Create(
        string id,
        string orderId,
        string eventType,
        string? actorId = null,
        string? actorRole = null,
        string? metadata = null)
    {
        return new OrderTimeline
        {
            Id = id,
            OrderId = orderId,
            EventType = eventType,
            ActorId = actorId,
            ActorRole = actorRole,
            Metadata = metadata,
            CreatedAt = DateTime.UtcNow
        };
    }
}
