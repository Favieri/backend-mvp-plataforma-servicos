namespace Domain.Entities;

public sealed record Service(
    string Id,
    string Name,
    string? Icon,
    DateTime CreatedAt,
    string? CategoryId = null,
    int? TierId = null);
