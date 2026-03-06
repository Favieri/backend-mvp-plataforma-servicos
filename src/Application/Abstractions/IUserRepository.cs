using Domain.Entities;

namespace Application.Abstractions;

public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken ct);
    Task<bool> ZoneExistsAndActiveAsync(string zoneId, CancellationToken ct);
    Task<object> CreateAsync(string name, string email, string? phone, string role, string hashedPassword, string? zoneId, CancellationToken ct);
}
