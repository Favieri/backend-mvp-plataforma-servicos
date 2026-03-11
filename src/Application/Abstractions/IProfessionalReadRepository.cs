using Application.DTOs;

namespace Application.Abstractions;

public interface IProfessionalReadRepository
{
    Task<IReadOnlyList<ProfessionalCardDto>> GetProfessionalsAsync(string? zoneId, string? serviceId, CancellationToken ct);
    Task<IReadOnlyList<ZoneDto>> GetZonesAsync(CancellationToken ct);
    Task<IReadOnlyList<ServiceDto>> GetServicesAsync(CancellationToken ct);
    Task<IReadOnlyList<ServiceDto>> GetServicesByCategoryAsync(string categoryId, CancellationToken ct);
}
