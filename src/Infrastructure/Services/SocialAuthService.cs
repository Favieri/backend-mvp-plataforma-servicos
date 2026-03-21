using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services;

public sealed class SocialAuthService : ISocialAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _googleClientId;
    private readonly string _facebookAppId;
    private readonly string _facebookAppSecret;

    public SocialAuthService(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _googleClientId = config["GOOGLE_CLIENT_ID"] ?? "";
        _facebookAppId = config["FACEBOOK_APP_ID"] ?? "";
        _facebookAppSecret = config["FACEBOOK_APP_SECRET"] ?? "";
    }

    public async Task<SocialUserInfo> ValidateGoogleTokenAsync(string idToken, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(
            $"https://oauth2.googleapis.com/tokeninfo?id_token={Uri.EscapeDataString(idToken)}", ct);

        if (!response.IsSuccessStatusCode)
            throw new SocialAuthException("Token do Google inválido ou expirado");

        var payload = await response.Content.ReadFromJsonAsync<GoogleTokenInfo>(ct)
            ?? throw new SocialAuthException("Falha ao decodificar resposta do Google");

        if (!string.Equals(payload.Aud, _googleClientId, StringComparison.Ordinal))
            throw new SocialAuthException("Token do Google não pertence a esta aplicação");

        if (!string.Equals(payload.EmailVerified, "true", StringComparison.OrdinalIgnoreCase))
            throw new SocialAuthException("E-mail do Google não está verificado");

        if (string.IsNullOrWhiteSpace(payload.Email))
            throw new SocialAuthException("Token do Google não contém e-mail");

        return new SocialUserInfo(
            ProviderUserId: payload.Sub ?? "",
            Email: payload.Email,
            Name: payload.Name ?? payload.Email,
            Provider: "google");
    }

    public async Task<SocialUserInfo> ValidateFacebookTokenAsync(string accessToken, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();

        // 1. Debug token to verify it belongs to our app
        var debugResponse = await client.GetAsync(
            $"https://graph.facebook.com/debug_token?input_token={Uri.EscapeDataString(accessToken)}&access_token={Uri.EscapeDataString(_facebookAppId)}|{Uri.EscapeDataString(_facebookAppSecret)}", ct);

        if (!debugResponse.IsSuccessStatusCode)
            throw new SocialAuthException("Falha ao validar token do Facebook");

        var debugResult = await debugResponse.Content.ReadFromJsonAsync<FacebookDebugTokenResponse>(ct)
            ?? throw new SocialAuthException("Falha ao decodificar resposta do Facebook");

        if (debugResult.Data is null || !debugResult.Data.IsValid)
            throw new SocialAuthException("AccessToken do Facebook inválido");

        if (!string.Equals(debugResult.Data.AppId, _facebookAppId, StringComparison.Ordinal))
            throw new SocialAuthException("AccessToken do Facebook emitido para outro aplicativo");

        // 2. Get user info
        var userResponse = await client.GetAsync(
            $"https://graph.facebook.com/me?fields=id,name,email&access_token={Uri.EscapeDataString(accessToken)}", ct);

        if (!userResponse.IsSuccessStatusCode)
            throw new SocialAuthException("Falha ao obter dados do usuário do Facebook");

        var userInfo = await userResponse.Content.ReadFromJsonAsync<FacebookUserInfo>(ct)
            ?? throw new SocialAuthException("Falha ao decodificar dados do usuário do Facebook");

        if (string.IsNullOrWhiteSpace(userInfo.Email))
            throw new SocialAuthException("Conta do Facebook não forneceu e-mail. Permissão de e-mail é obrigatória. Tente outro método de login.");

        return new SocialUserInfo(
            ProviderUserId: userInfo.Id ?? "",
            Email: userInfo.Email,
            Name: userInfo.Name ?? userInfo.Email,
            Provider: "facebook");
    }

    // ─── DTOs for Google tokeninfo response ─────────────────────────────────

    private sealed class GoogleTokenInfo
    {
        [JsonPropertyName("sub")]
        public string? Sub { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("email_verified")]
        public string? EmailVerified { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("aud")]
        public string? Aud { get; set; }
    }

    // ─── DTOs for Facebook responses ────────────────────────────────────────

    private sealed class FacebookDebugTokenResponse
    {
        [JsonPropertyName("data")]
        public FacebookDebugData? Data { get; set; }
    }

    private sealed class FacebookDebugData
    {
        [JsonPropertyName("is_valid")]
        public bool IsValid { get; set; }

        [JsonPropertyName("app_id")]
        public string? AppId { get; set; }
    }

    private sealed class FacebookUserInfo
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }
}

/// <summary>
/// Exception thrown when social authentication fails (invalid token, missing email, etc.).
/// </summary>
public sealed class SocialAuthException : Exception
{
    public SocialAuthException(string message) : base(message) { }
}
