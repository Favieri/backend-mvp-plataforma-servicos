namespace Domain.Entities;

public sealed record Zone(
    string Id,
    string Name,
    bool Active,
    DateTime CreatedAt);
