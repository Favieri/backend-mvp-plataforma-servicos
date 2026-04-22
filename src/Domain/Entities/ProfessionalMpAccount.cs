namespace Domain.Entities;

public sealed record ProfessionalMpAccount(
    string Id,
    string ProfessionalId,
    long MpUserId,
    string MpAccessToken,
    string MpRefreshToken,
    DateTime MpTokenExpiresAt,
    string Status,
    bool LiveMode,
    DateTime CreatedAt,
    DateTime UpdatedAt);
