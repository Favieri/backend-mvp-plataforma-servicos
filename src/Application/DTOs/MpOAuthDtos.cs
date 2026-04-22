namespace Application.DTOs;

public sealed record MpTokenResponse(
    string AccessToken,
    string RefreshToken,
    long UserId,
    int ExpiresIn,
    string? Scope,
    bool LiveMode);
