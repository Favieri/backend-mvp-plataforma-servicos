namespace Application.Abstractions;

public interface IProfessionalDetailRepository
{
    Task<object?> GetByIdAsync(string id, CancellationToken ct);
    Task<object?> UpdateAsync(string id, string? bio, bool? active, string? availabilityText, string? avatarUrl, CancellationToken ct);
    Task<IReadOnlyList<object>> GetZonesAsync(string professionalId, CancellationToken ct);
    Task<object?> UpdateZonesAsync(string professionalId, string[] zoneIds, CancellationToken ct);
}
