namespace Domain.Entities;

public sealed record User(
    string Id,
    string Name,
    string Email,
    string? Phone,
    string Role,
    string? ZoneId,
    DateTime CreatedAt);
