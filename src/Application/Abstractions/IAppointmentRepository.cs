using Domain.Entities;

namespace Application.Abstractions;

public interface IAppointmentRepository
{
    Task<IReadOnlyList<Appointment>> GetByClientAsync(string clientId, CancellationToken ct);
    Task<IReadOnlyList<object>> GetByProfessionalAsync(string professionalId, string? status, DateTime? from, DateTime? to, CancellationToken ct);
    Task<bool> HasConflictAsync(string professionalId, DateTime startsAt, DateTime endsAt, CancellationToken ct);
    Task<(int? SlotMinutes, bool? AllowInstantBooking)> GetProfessionalConfigAsync(string professionalId, CancellationToken ct);
    Task<bool> ProfessionalExistsAsync(string professionalId, CancellationToken ct);
    Task<Appointment> CreateAsync(Appointment appointment, CancellationToken ct);
    Task<Appointment?> UpdateStatusAsync(string id, string status, CancellationToken ct);
    Task<object?> GetAppointmentWithParticipantsAsync(string id, CancellationToken ct);
}
