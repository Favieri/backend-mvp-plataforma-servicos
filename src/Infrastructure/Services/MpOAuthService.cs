using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed class MpOAuthService : IMpOAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MpOAuthService> _logger;
    private readonly string _appId;
    private readonly string _clientSecret;
    private readonly string _redirectUri;

    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    public MpOAuthService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IConfiguration config,
        ILogger<MpOAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
        _appId = config["MercadoPago__AppId"] ?? "";
        _clientSecret = config["MercadoPago__ClientSecret"] ?? "";
        _redirectUri = config["MercadoPago__RedirectUri"] ?? "";
    }

    public (string AuthUrl, string StateToken) BuildAuthorizationUrl(string professionalId)
    {
        var raw = $"{professionalId}:{Guid.NewGuid()}";
        var stateToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));

        _cache.Set(
            $"mp:oauth:state:{stateToken}",
            professionalId,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = StateTtl });

        var authUrl = "https://auth.mercadopago.com/authorization" +
            $"?client_id={Uri.EscapeDataString(_appId)}" +
            "&response_type=code" +
            "&platform_id=mp" +
            $"&redirect_uri={Uri.EscapeDataString(_redirectUri)}" +
            $"&state={Uri.EscapeDataString(stateToken)}";

        return (authUrl, stateToken);
    }

    public string? ValidateAndConsumeState(string stateToken)
    {
        var key = $"mp:oauth:state:{stateToken}";
        if (!_cache.TryGetValue(key, out string? professionalId))
            return null;
        _cache.Remove(key);
        return professionalId;
    }

    public async Task<MpTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var payload = new Dictionary<string, string>
        {
            ["client_id"] = _appId,
            ["client_secret"] = _clientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _redirectUri,
        };

        return await PostTokenAsync(payload, ct);
    }

    public async Task<MpTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        var payload = new Dictionary<string, string>
        {
            ["client_id"] = _appId,
            ["client_secret"] = _clientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };

        return await PostTokenAsync(payload, ct);
    }

    public async Task TryRevokeTokenAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Delete, "https://api.mercadopago.com/oauth/token");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MpOAuth] TryRevokeTokenAsync failed (best-effort, ignored)");
        }
    }

    // ─── Token masking (for logs — tokens must never appear unmasked) ────────────
    internal static string MaskToken(string token) =>
        token.Length <= 12 ? "****" : $"{token[..8]}****{token[^4..]}";

    // ─── Shared token exchange logic ─────────────────────────────────────────────
    private async Task<MpTokenResponse> PostTokenAsync(Dictionary<string, string> payload, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsync(
            "https://api.mercadopago.com/oauth/token",
            new FormUrlEncodedContent(payload),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            _logger.LogWarning("[MpOAuth] Token request failed. Status={StatusCode}", statusCode);
            throw new MpOAuthException($"MP /oauth/token responded with {statusCode}", statusCode);
        }

        var raw = await response.Content.ReadFromJsonAsync<MpRawTokenResponse>(cancellationToken: ct)
            ?? throw new MpOAuthException("Empty response body from MP /oauth/token");

        if (string.IsNullOrEmpty(raw.AccessToken))
            throw new MpOAuthException("MP /oauth/token returned an empty access_token");

        _logger.LogInformation(
            "[MpOAuth] Token obtained. UserId={UserId} Masked={Masked} ExpiresIn={ExpiresIn}s LiveMode={LiveMode}",
            raw.UserId, MaskToken(raw.AccessToken ?? ""), raw.ExpiresIn, raw.LiveMode);

        return new MpTokenResponse
        {
            AccessToken = raw.AccessToken ?? "",
            RefreshToken = raw.RefreshToken ?? "",
            ExpiresIn = raw.ExpiresIn,
            UserId = raw.UserId,
            LiveMode = raw.LiveMode,
        };
    }

    // ─── Private response DTO ─────────────────────────────────────────────────────
    private sealed class MpRawTokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public long ExpiresIn { get; set; }
        [JsonPropertyName("user_id")] public long UserId { get; set; }
        [JsonPropertyName("live_mode")] public bool LiveMode { get; set; }
    }
}
