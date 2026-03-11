namespace Application.DTOs;

public sealed record ZoneDto(string Id, string Name);

public sealed record ServiceDto(string Id, string Name, string? Icon, string? CategoryId = null, int? TierId = null);

public sealed record HomeBootstrapDto(
    IReadOnlyList<ProfessionalCardDto> Professionals,
    IReadOnlyList<ZoneDto> Zones,
    IReadOnlyList<ServiceDto> Services,
    IReadOnlyList<CategoryDto>? Categories = null,
    IReadOnlyList<TierDto>? Tiers = null);
