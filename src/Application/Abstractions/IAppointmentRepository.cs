using Domain.Entities;

namespace Application.Abstractions;

public interface IAppointmentRepository
{
    Task<IReadOnlyList<Appointment>> GetByClientAsync(string clientId, CancellationToken ct);
    Task<Appointment> CreateAsync(Appointment appointment, CancellationToken ct);
    Task<Appointment?> UpdateStatusAsync(string id, string status, CancellationToken ct);
}
