using Domain.Entities;

namespace Application.Abstractions;

public interface IAvailabilityRepository
{
    Task<IReadOnlyList<object>> GetByProfessionalAsync(string professionalId, CancellationToken ct);
    Task SaveAllAsync(string professionalId, IReadOnlyList<(int Weekday, int StartMinutes, int EndMinutes, bool Active)> rows, CancellationToken ct);
    Task<IReadOnlyList<object>> GetBlocksAsync(string professionalId, DateTime from, DateTime to, CancellationToken ct);
    Task<object> CreateBlockAsync(string professionalId, DateTime startsAt, DateTime endsAt, string? reason, CancellationToken ct);
    Task<bool> ProfessionalExistsAsync(string professionalId, CancellationToken ct);
    Task<object?> GetProfessionalSchedulingConfigAsync(string professionalId, CancellationToken ct);
    Task<IReadOnlyList<ProfessionalAvailability>> GetAvailabilityForDayAsync(string professionalId, int weekday, CancellationToken ct);
    Task<IReadOnlyList<Appointment>> GetAppointmentsForDayAsync(string professionalId, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken ct);
    Task<IReadOnlyList<ProfessionalBlock>> GetBlocksForDayAsync(string professionalId, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken ct);
}
