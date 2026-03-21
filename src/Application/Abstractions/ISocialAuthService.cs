namespace Application.Abstractions;

public interface ISocialAuthService
{
    Task<SocialUserInfo> ValidateGoogleTokenAsync(string idToken, CancellationToken ct);
    Task<SocialUserInfo> ValidateFacebookTokenAsync(string accessToken, CancellationToken ct);
}

public sealed record SocialUserInfo(string ProviderUserId, string Email, string Name, string Provider);
