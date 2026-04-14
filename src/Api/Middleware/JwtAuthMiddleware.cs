using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace Api.Middleware;

/// <summary>
/// Validates application HS256 JWTs and populates HttpContext.User.
/// Coexists with bcrypt manual auth — does not block requests on failure.
/// </summary>
public sealed class JwtAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<JwtAuthMiddleware> logger)
{
    private static readonly JwtSecurityTokenHandler _handler = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader is not null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            var jwtSecret = configuration["JWT_SECRET"];

            if (!string.IsNullOrWhiteSpace(jwtSecret))
            {
                try
                {
                    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                    var validationParams = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = key,
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromSeconds(30)
                    };

                    var principal = _handler.ValidateToken(token, validationParams, out _);
                    context.User = principal;
                }
                catch (Exception ex)
                {
                    logger.LogDebug("JWT validation failed: {Message}", ex.Message);
                    // Not blocking — let the request proceed; endpoints requiring auth will return 401
                }
            }
            else
            {
                logger.LogDebug("JWT_SECRET not configured — JWT validation skipped");
            }
        }

        await next(context);
    }
}
