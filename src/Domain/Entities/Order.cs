namespace Domain.Entities;

public class Order
{
    public string Id { get; private set; } = default!;
    public string ClientId { get; private set; } = default!;
    public string ServiceId { get; private set; } = default!;
    public string? Description { get; private set; }
    public string? Location { get; private set; }
    public DateTime? Date { get; private set; }
    public string Status { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }

    private Order() { }

    public static Order Create(
        string id,
        string clientId,
        string serviceId,
        string? description,
        string? location,
        DateTime? date)
    {
        return new Order
        {
            Id = id,
            ClientId = clientId,
            ServiceId = serviceId,
            Description = description,
            Location = location,
            Date = date,
            Status = "aberto",
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateStatus(string newStatus) => Status = newStatus;
}
