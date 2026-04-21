using Application.DTOs;

namespace Application.Abstractions;

public interface IProfessionalReadRepository
{
    Task<IReadOnlyList<ProfessionalCardDto>> GetProfessionalsAsync(string? zoneId, string? serviceId, string? professionalId, CancellationToken ct);

    /// <summary>Phase 5: suporta filtros de verificationStatus e minRating além dos filtros base.</summary>
    Task<IReadOnlyList<ProfessionalCardDto>> GetProfessionalsFilteredAsync(
        string? zoneId,
        string? serviceId,
        string? verificationStatus,
        double? minRating,
        string? professionalId,
        CancellationToken ct);

    Task<IReadOnlyList<ZoneDto>> GetZonesAsync(CancellationToken ct);
    Task<IReadOnlyList<ServiceDto>> GetServicesAsync(CancellationToken ct);
    Task<IReadOnlyList<ServiceDto>> GetServicesByCategoryAsync(string categoryId, CancellationToken ct);
}
