using System.Security.Cryptography;
using System.Text;

namespace Application.Services;

/// <summary>
/// Geração/validação de tokens de confirmação de conta e recuperação de senha.
/// O token em si nunca é persistido — apenas seu hash SHA-256 (ver account_token.token_hash).
/// </summary>
public static class AccountTokenService
{
    public const string EmailVerificationType = "email_verification";
    public const string PasswordResetType = "password_reset";

    public static readonly TimeSpan EmailVerificationTtl = TimeSpan.FromHours(48);
    public static readonly TimeSpan PasswordResetTtl = TimeSpan.FromMinutes(30);

    // 32 bytes aleatórios, codificados em base64url — suficiente para não ser adivinhável
    public static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
