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
    public IReadOnlyList<ZoneDto> Zones { get; set; } = [];

    // Phase 5: trust metrics + verification
    public string VerificationStatus { get; init; } = "pending";
    public IReadOnlyList<string> Badges { get; init; } = [];
    public double? ResponseRate { get; init; }
    public int? AvgResponseTimeMinutes { get; init; }
    public double? CompletionRate { get; init; }
}

public sealed class ProfessionalServiceDto
{
    public string Id { get; init; } = string.Empty;
    public string ServiceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double? Price { get; init; }
    public string? Description { get; init; }
    public int? TierId { get; init; }
    public string? ContractMode { get; init; }
    public int? DurationMinutes { get; init; }
    public int? MinLeadTimeMinutes { get; init; }
    public string? TipoContratacao { get; init; }
    public string? TipoPrecificacao { get; init; }
}
