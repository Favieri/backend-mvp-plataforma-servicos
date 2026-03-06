namespace Domain.Entities;

public sealed record ProfessionalOrderIgnore(
    string ProfessionalId,
    string OrderId,
    DateTime CreatedAt);
