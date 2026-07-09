namespace Application.Abstractions;

public interface IProfessionalDetailRepository
{
    Task<bool> UserExistsAsync(string userId, CancellationToken ct);
    Task<bool> ProfessionalExistsByUserIdAsync(string userId, CancellationToken ct);
    Task<bool> ExistsAsync(string id, CancellationToken ct);
    Task<bool> ZonesExistAndActiveAsync(string[] zoneIds, CancellationToken ct);
    Task<object> CreateAsync(string userId, string? bio, bool active, string[] zoneIds, CancellationToken ct);
    Task<object?> GetByIdAsync(string id, CancellationToken ct);
    Task<object?> UpdateAsync(string id, string? bio, bool? active, string? availabilityText, string? avatarUrl, CancellationToken ct);
    Task<bool> UpdateAvatarUrlAsync(string id, string avatarUrl, CancellationToken ct);
    Task<IReadOnlyList<object>> GetZonesAsync(string professionalId, CancellationToken ct);
    Task<object?> UpdateZonesAsync(string professionalId, string[] zoneIds, CancellationToken ct);
}
