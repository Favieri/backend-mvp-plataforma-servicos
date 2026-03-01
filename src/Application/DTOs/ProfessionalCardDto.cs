namespace Application.DTOs;

public sealed class ProfessionalCardDto
{
    public string Id { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
    public double? Rating { get; init; }
    public bool Active { get; init; }
    public int? CompletedJobsCount { get; init; }
    public string? AvailabilityText { get; init; }
    public IReadOnlyList<ProfessionalServiceDto> Services { get; set; } = [];
    public IReadOnlyList<string> Zones { get; set; } = [];
}

public sealed class ProfessionalServiceDto
{
    public string Id { get; init; } = string.Empty;
    public string ServiceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double Price { get; init; }
    public string? Description { get; init; }
}
