namespace Domain.Entities;

public sealed record Review(
    string Id,
    string OrderId,
    string ProfessionalId,
    string ClientId,
    int Rating,
    string? Comment,
    DateTime CreatedAt,
    // ─── Phase 3: expanded ratings (1-5 each, nullable) ─────────────────────
    int? PunctualityRating = null,
    int? QualityRating = null,
    int? CommunicationRating = null,
    int? CleanlinessRating = null,
    // ─── Phase 3: photos ─────────────────────────────────────────────────────
    string? PhotoUrls = null,               // JSONB stored as string
    // ─── Phase 3: professional reviews client ────────────────────────────────
    string? ProfessionalReviewOfClient = null,
    int? ProfessionalRatingOfClient = null,
    // ─── Phase 3: double-blind visibility ────────────────────────────────────
    DateTime? ClientVisibleAt = null,
    DateTime? ProfessionalVisibleAt = null,
    // ─── Phase 3: verified (linked to paid completed order) ──────────────────
    bool IsVerified = false);
