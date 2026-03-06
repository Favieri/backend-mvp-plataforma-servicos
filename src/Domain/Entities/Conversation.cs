namespace Domain.Entities;

public sealed record Conversation(
    string Id,
    string? OrderId,
    string ClientId,
    string ProfessionalId,
    DateTime CreatedAt,
    DateTime? ClientLastReadAt,
    DateTime? ProfessionalLastReadAt);
