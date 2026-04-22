namespace Application.DTOs;

public sealed class ProfessionalMpAccountDto
{
    public string Id { get; init; } = string.Empty;
    public string ProfessionalId { get; init; } = string.Empty;
    public long MpUserId { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool LiveMode { get; init; }
    public DateTime MpTokenExpiresAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    // MpAccessToken and MpRefreshToken are deliberately excluded to prevent accidental exposure
}
