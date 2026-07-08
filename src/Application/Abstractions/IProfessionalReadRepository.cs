using Application.DTOs;

namespace Application.Abstractions;

public interface IProfessionalReadRepository
{
    Task<PagedResult<ProfessionalCardDto>> GetProfessionalsAsync(
        string? zoneId, string? serviceId, string? professionalId, int page, int pageSize, CancellationToken ct);

    /// <summary>Phase 5: suporta filtros de verificationStatus e minRating além dos filtros base.</summary>
    Task<PagedResult<ProfessionalCardDto>> GetProfessionalsFilteredAsync(
        string? zoneId,
        string? serviceId,
        string? verificationStatus,
        double? minRating,
        string? professionalId,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<IReadOnlyList<ZoneDto>> GetZonesAsync(CancellationToken ct);
    Task<IReadOnlyList<ServiceDto>> GetServicesAsync(CancellationToken ct);
    Task<IReadOnlyList<ServiceDto>> GetServicesByCategoryAsync(string categoryId, CancellationToken ct);
}
