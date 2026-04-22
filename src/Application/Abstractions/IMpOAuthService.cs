using Application.DTOs;

namespace Application.Abstractions;

public interface IMpOAuthService
{
    Task<(string connectUrl, int expiresInSeconds)> GetConnectUrlAsync(string professionalId, CancellationToken ct);
    Task<string?> ValidateAndConsumeStateAsync(string state, CancellationToken ct);
    Task<MpTokenResponse> ExchangeCodeForTokensAsync(string code, CancellationToken ct);
    Task<string> GetValidAccessTokenAsync(string professionalId, CancellationToken ct);
}
