namespace Domain.Entities;

public sealed record ProfessionalPortfolio(
    string Id,
    string ProfessionalId,
    string ImageUrl,
    string? Title,
    string? Description,
    int? OrderIndex,
    DateTime CreatedAt);
