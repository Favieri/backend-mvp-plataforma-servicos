namespace Application.DTOs;

// ─── Verification response ────────────────────────────────────────────────────

public sealed class ProfessionalVerificationDto
{
    public string Id { get; init; } = string.Empty;
    public string ProfessionalId { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentUrl { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public DateTime SubmittedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

// ─── Trust metrics ────────────────────────────────────────────────────────────

public sealed class TrustMetricsDto
{
    public double? ResponseRate { get; init; }
    public int? AvgResponseTimeMinutes { get; init; }
    public double? CompletionRate { get; init; }
    public IReadOnlyList<string> Badges { get; init; } = [];
}

// ─── Requests ─────────────────────────────────────────────────────────────────

public sealed class SubmitVerificationRequest
{
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentUrl { get; init; } = string.Empty;
}

public sealed class ReviewVerificationRequest
{
    public string Status { get; init; } = string.Empty;   // in_review | verified | rejected
    public string? Notes { get; init; }
}
