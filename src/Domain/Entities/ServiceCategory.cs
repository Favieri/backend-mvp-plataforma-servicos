namespace Domain.Entities;

public sealed record ServiceCategory(
    string Id,
    string Name,
    string? Icon,
    DateTime CreatedAt);
