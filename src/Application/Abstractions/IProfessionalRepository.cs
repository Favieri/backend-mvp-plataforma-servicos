using Application.DTOs;

namespace Application.Abstractions;

public interface IProfessionalRepository
{
    Task<IReadOnlyList<ProfessionalCardDto>> GetProfessionalCardsAsync(
        string? serviceId,
        string? zoneId,
        string? excludeProfessionalId,
        string? professionalId,
        bool filterZones,
        CancellationToken ct);
}
