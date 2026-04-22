namespace Application.Abstractions;

public interface IMpOAuthService
{
    /// <summary>
    /// Generates the MP authorization URL and caches the anti-CSRF state token
    /// under key "mp:oauth:state:{stateToken}" for 10 minutes.
    /// </summary>
    (string AuthUrl, string StateToken) BuildAuthorizationUrl(string professionalId);

    /// <summary>
    /// Validates the state token from the callback. Returns the professionalId if valid,
    /// null if expired or unknown. Removes the cache entry (one-time use).
    /// </summary>
    string? ValidateAndConsumeState(string stateToken);

    /// <summary>
    /// Exchanges an authorization code for tokens via MP /oauth/token.
    /// Throws <see cref="MpOAuthException"/> on failure.
    /// </summary>
    Task<MpTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct);

    /// <summary>
    /// Refreshes tokens using grant_type=refresh_token.
    /// Throws <see cref="MpOAuthException"/> with StatusCode=401 when the refresh token is revoked.
    /// </summary>
    Task<MpTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct);

    /// <summary>
    /// Best-effort token revocation at MP. Never throws — logs warnings on failure.
    /// </summary>
    Task TryRevokeTokenAsync(string accessToken, CancellationToken ct);
}

public sealed class MpTokenResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public long ExpiresIn { get; init; }
    public long UserId { get; init; }
    public bool LiveMode { get; init; }
}

public sealed class MpOAuthException : Exception
{
    public int StatusCode { get; }
    public MpOAuthException(string message, int statusCode = 0) : base(message)
        => StatusCode = statusCode;
}
