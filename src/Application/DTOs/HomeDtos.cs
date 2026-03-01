namespace Application.DTOs;

public sealed record ZoneDto(string Id, string Name);

public sealed record ServiceDto(string Id, string Name, string? Icon);

public sealed record ProfessionalServiceDto(
    string Id,
    string ServiceId,
    string Name,
    decimal Price,
    string? Description,
    string? Icon);

public sealed record ProfessionalCardDto(
    string Id,
    string UserId,
    string Name,
    string? AvatarUrl,
    decimal? Rating,
    bool Active,
    int CompletedJobsCount,
    string? AvailabilityText,
    IReadOnlyList<ProfessionalServiceDto> Services,
    IReadOnlyList<ZoneDto> Zones);

public sealed record HomeBootstrapDto(
    IReadOnlyList<ProfessionalCardDto> Professionals,
    IReadOnlyList<ZoneDto> Zones,
    IReadOnlyList<ServiceDto> Services);
