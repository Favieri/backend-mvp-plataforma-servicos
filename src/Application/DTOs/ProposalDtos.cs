namespace Application.DTOs;

public sealed record ProposalDto(
    string Id,
    string? OrderId,
    string? SourceOrderId,
    string ProfessionalId,
    string ClientId,
    string ServiceId,
    string? ProfessionalServiceId,
    string? ConversationId,
    string Scope,
    string? IncludesDescription,
    string? ExcludesDescription,
    int PriceTotalCents,
    string? PriceByStage,
    string? DurationEstimate,
    DateTime? SuggestedDatetime,
    int VisitFeeCents,
    DateTime ValidUntil,
    string Status,
    string? RejectionReason,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ProposalSummaryDto(
    string Id,
    string ServiceId,
    int PriceTotalCents,
    string? DurationEstimate,
    DateTime ValidUntil,
    string Status,
    DateTime CreatedAt);
