namespace Domain.Entities;

public sealed record ProfessionalAvailability(
    string Id,
    string ProfessionalId,
    int Weekday,
    int StartMinutes,
    int EndMinutes,
    bool Active);
