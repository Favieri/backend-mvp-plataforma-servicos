using Application.DTOs;

namespace Application.Abstractions;

public interface IProfessionalMpAccountRepository
{
    /// <summary>Returns the MP account DTO for the professional, or null if none exists.</summary>
    Task<ProfessionalMpAccountDto?> GetByProfessionalIdAsync(string professionalId, CancellationToken ct);

    /// <summary>
    /// Upserts the MP account row and sets Professional.MpConnected=true, MpConnectedAt=UtcNow.
    /// </summary>
    Task UpsertAsync(
        string professionalId,
        long mpUserId,
        string accessToken,
        string refreshToken,
        DateTime expiresAt,
        bool liveMode,
        CancellationToken ct);

    /// <summary>
    /// Sets status='revoked' and Professional.MpConnected=false.
    /// Returns false if no account existed.
    /// </summary>
    Task<bool> RevokeAsync(string professionalId, CancellationToken ct);

    /// <summary>Returns active accounts whose token expires before <paramref name="expiresBeforeUtc"/>.</summary>
    Task<IReadOnlyList<ProfessionalMpAccountDto>> GetExpiringSoonAsync(DateTime expiresBeforeUtc, CancellationToken ct);

    /// <summary>
    /// Reads the refresh token internally, calls mpService.RefreshTokenAsync, then updates stored tokens.
    /// Throws MpOAuthException(401) if the refresh token is invalid — caller should then call MarkExpiredAsync.
    /// </summary>
    Task RefreshAndUpdateAsync(string professionalId, IMpOAuthService mpService, CancellationToken ct);

    /// <summary>
    /// Best-effort: reads the access token internally, calls mpService.TryRevokeTokenAsync,
    /// then calls RevokeAsync. Never throws on revocation failure.
    /// </summary>
    Task TryRevokeAndDisconnectAsync(string professionalId, IMpOAuthService mpService, CancellationToken ct);

    /// <summary>Sets status='expired' and Professional.MpConnected=false.</summary>
    Task MarkExpiredAsync(string professionalId, CancellationToken ct);
}
