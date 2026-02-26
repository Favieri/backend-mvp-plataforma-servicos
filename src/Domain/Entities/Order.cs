namespace Domain.Entities;

public sealed record Order(
    string Id,
    string ClientId,
    string ServiceId,
    string? Description,
    string? Location,
    DateTime? Date,
    string Status,
    DateTime CreatedAt);
