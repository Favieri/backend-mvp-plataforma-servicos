using Application.DTOs;

namespace Application.Abstractions;

public interface IMpOAuthService
{
    Task<(string connectUrl, int expiresInSeconds)> GetConnectUrlAsync(string professionalId, CancellationToken ct);
    Task<(string? ProfessionalId, string? CodeVerifier)> ValidateAndConsumeStateAsync(string state, CancellationToken ct);
    Task<MpTokenResponse> ExchangeCodeForTokensAsync(string code, string? codeVerifier, CancellationToken ct);
    Task<string> GetValidAccessTokenAsync(string professionalId, CancellationToken ct);
}
