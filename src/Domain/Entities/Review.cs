namespace Domain.Entities;

public sealed record Review(
    string Id,
    string OrderId,
    string ProfessionalId,
    string ClientId,
    int Rating,
    string? Comment,
    DateTime CreatedAt);
