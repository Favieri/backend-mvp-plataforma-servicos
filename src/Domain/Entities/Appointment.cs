namespace Domain.Entities;

public sealed record Appointment(
    string Id,
    string ProfessionalId,
    string? ClientId,
    string? ServiceId,
    DateTime StartsAt,
    DateTime EndsAt,
    string Status,
    string? Location,
    string? Notes);
