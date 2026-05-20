using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed class MpOAuthService : IMpOAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IProfessionalMpAccountRepository _mpRepo;
    private readonly AppDbContext _ctx;
    private readonly ILogger<MpOAuthService> _logger;

    private readonly string _appId;
    private readonly string _clientSecret;
    private readonly string _redirectUri;
    private readonly string _frontendBaseUrl;

    private const int StateExpirySeconds = 600;

    public MpOAuthService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IProfessionalMpAccountRepository mpRepo,
        AppDbContext ctx,
        IConfiguration config,
        ILogger<MpOAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _mpRepo = mpRepo;
        _ctx = ctx;
        _logger = logger;

        _appId         = config["MercadoPago:AppId"]          ?? "";
        _clientSecret  = config["MercadoPago:ClientSecret"]   ?? "";
        _redirectUri   = config["MercadoPago:RedirectUri"]    ?? "";
        _frontendBaseUrl = config["MercadoPago:FrontendBaseUrl"] ?? "";
    }

    public Task<(string connectUrl, int expiresInSeconds)> GetConnectUrlAsync(string professionalId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_appId))
            throw new InvalidOperationException(
                "MercadoPago__AppId não configurado. Configure a variável de ambiente.");

        if (string.IsNullOrWhiteSpace(_redirectUri))
            throw new InvalidOperationException(
                "MercadoPago__RedirectUri não configurado. Configure a variável de ambiente.");

        var raw = $"{professionalId}:{Guid.NewGuid()}";
        var state = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        _cache.Set($"mp:state:{state}", professionalId, TimeSpan.FromSeconds(StateExpirySeconds));

        var codeVerifier  = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        _cache.Set($"mp:verifier:{state}", codeVerifier, TimeSpan.FromSeconds(StateExpirySeconds));

        var url = $"https://auth.mercadopago.com.br/authorization" +
                  $"?client_id={Uri.EscapeDataString(_appId)}" +
                  $"&response_type=code" +
                  $"&platform_id=mp" +
                  $"&state={Uri.EscapeDataString(state)}" +
                  $"&redirect_uri={Uri.EscapeDataString(_redirectUri)}" +
                  $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                  $"&code_challenge_method=S256";

        return Task.FromResult((url, StateExpirySeconds));
    }

    public Task<(string? ProfessionalId, string? CodeVerifier)> ValidateAndConsumeStateAsync(string state, CancellationToken ct)
    {
        if (!_cache.TryGetValue($"mp:state:{state}", out string? professionalId) || professionalId is null)
            return Task.FromResult<(string?, string?)>((null, null));

        _cache.TryGetValue($"mp:verifier:{state}", out string? codeVerifier);

        _cache.Remove($"mp:state:{state}");
        _cache.Remove($"mp:verifier:{state}");

        return Task.FromResult<(string?, string?)>((professionalId, codeVerifier));
    }

    public async Task<MpTokenResponse> ExchangeCodeForTokensAsync(string code, string? codeVerifier, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("mercadopago");

        var payload = new
        {
            client_id = _appId,
            client_secret = _clientSecret,
            grant_type = "authorization_code",
            code,
            redirect_uri = _redirectUri,
            code_verifier = codeVerifier
        };

        using var response = await client.PostAsJsonAsync("/oauth/token", payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("[MpOAuth] Token exchange failed. Status={Status} Body={Body}",
                response.StatusCode, body);
            throw new MpOAuthException($"MP token exchange failed: {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<MpTokenApiResponse>(ct)
            ?? throw new MpOAuthException("Empty response from MP token endpoint");

        return new MpTokenResponse(
            AccessToken: result.AccessToken,
            RefreshToken: result.RefreshToken,
            UserId: result.UserId,
            ExpiresIn: result.ExpiresIn,
            Scope: result.Scope,
            LiveMode: result.LiveMode);
    }

    public async Task<string> GetValidAccessTokenAsync(string professionalId, CancellationToken ct)
    {
        var account = await _mpRepo.GetByProfessionalIdAsync(professionalId, ct)
            ?? throw new MpAccountNotConnectedException(professionalId);

        if (account.MpTokenExpiresAt <= DateTime.UtcNow.AddHours(24))
            await RefreshTokenAsync(account, ct);

        // Re-read after potential refresh
        var refreshed = await _mpRepo.GetByProfessionalIdAsync(professionalId, ct)
            ?? throw new MpAccountNotConnectedException(professionalId);

        return refreshed.MpAccessToken;
    }

    private async Task RefreshTokenAsync(ProfessionalMpAccount account, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("mercadopago");

        var payload = new
        {
            client_id = _appId,
            client_secret = _clientSecret,
            grant_type = "refresh_token",
            refresh_token = account.MpRefreshToken
        };

        try
        {
            using var response = await client.PostAsJsonAsync("/oauth/token", payload, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("[MpOAuth] Refresh token rejected for professional {ProfessionalId}. Marking as expired.", account.ProfessionalId);
                await MarkAsExpiredAsync(account.ProfessionalId, ct);
                throw new MpOAuthException("Refresh token rejected by MP");
            }

            if (!response.IsSuccessStatusCode)
                throw new MpOAuthException($"MP refresh failed: {response.StatusCode}");

            var result = await response.Content.ReadFromJsonAsync<MpTokenApiResponse>(ct)
                ?? throw new MpOAuthException("Empty response from MP refresh endpoint");

            var newExpiry = DateTime.UtcNow.AddSeconds(result.ExpiresIn);

            _logger.LogInformation("[MpOAuth] Refreshed token for professional {ProfessionalId}. NewExpiry={Expiry} Token={Masked}",
                account.ProfessionalId, newExpiry, MaskToken(result.AccessToken));

            await _mpRepo.UpdateTokensAsync(account.ProfessionalId, result.AccessToken, result.RefreshToken, newExpiry, ct);
        }
        catch (MpOAuthException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MpOAuth] Unexpected error refreshing token for professional {ProfessionalId}", account.ProfessionalId);
            throw new MpOAuthException($"Token refresh error: {ex.Message}", ex);
        }
    }

    private async Task MarkAsExpiredAsync(string professionalId, CancellationToken ct)
    {
        await _mpRepo.UpdateStatusAsync(professionalId, "expired", ct);
        await _ctx.Professionals
            .Where(p => p.Id == professionalId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.MpConnected, false), ct);
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(hash)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string MaskToken(string token)
    {
        if (token.Length <= 20)
            return new string('*', Math.Max(token.Length, 4));
        return token[..^20] + "********************";
    }

    private sealed class MpTokenApiResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = "";

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = "";

        [JsonPropertyName("user_id")]
        public long UserId { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("scope")]
        public string? Scope { get; init; }

        [JsonPropertyName("live_mode")]
        public bool LiveMode { get; init; }
    }
}

public sealed class MpOAuthException : Exception
{
    public MpOAuthException(string message) : base(message) { }
    public MpOAuthException(string message, Exception inner) : base(message, inner) { }
}

public sealed class MpAccountNotConnectedException : Exception
{
    public string ProfessionalId { get; }
    public MpAccountNotConnectedException(string professionalId)
        : base($"Professional {professionalId} does not have a connected Mercado Pago account.")
    {
        ProfessionalId = professionalId;
    }
}
