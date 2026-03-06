namespace Domain.Entities;

public sealed record ProfessionalBlock(
    string Id,
    string ProfessionalId,
    DateTime StartsAt,
    DateTime EndsAt,
    string? Reason,
    DateTime CreatedAt);
