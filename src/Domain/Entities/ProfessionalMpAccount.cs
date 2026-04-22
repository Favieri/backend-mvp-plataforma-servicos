namespace Domain.Entities;

public sealed record ProfessionalMpAccount(
    Guid Id,
    string ProfessionalId,       // text FK → "Professional"."id"
    string MpUserId,
    string MpAccessToken,        // NOTE: encrypt in production (tech debt)
    string MpRefreshToken,       // NOTE: encrypt in production (tech debt)
    DateTime MpTokenExpiresAt,
    string? MpScope,
    bool MpLiveMode,
    string Status,               // active | expired | revoked
    DateTime ConnectedAt,
    DateTime? LastRefreshedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
