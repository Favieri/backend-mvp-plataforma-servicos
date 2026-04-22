using Application.Abstractions;
using Infrastructure.BackgroundJobs;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Api.Extensions;

public static class MpOAuthEndpoints
{
    private const int CallbackRateLimitMax = 10;
    private static readonly TimeSpan CallbackRateLimitWindow = TimeSpan.FromMinutes(1);

    public static IEndpointRouteBuilder MapMpOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── GET /api/professionals/{professionalId}/mp/connect-url ───────────────
        // JWT required. Validates that the caller owns the professionalId.
        app.MapGet("/api/professionals/{professionalId}/mp/connect-url", async (
            string professionalId,
            HttpContext context,
            AppDbContext db,
            IMpOAuthService mpService,
            CancellationToken ct) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Json(new { error = "Não autorizado" }, statusCode: 401);

            var owns = await db.Professionals
                .AsNoTracking()
                .AnyAsync(p => p.Id == professionalId && p.UserId == userId, ct);
            if (!owns)
                return Results.Json(new { error = "Profissional não encontrado ou sem permissão" }, statusCode: 403);

            var (authUrl, _) = mpService.BuildAuthorizationUrl(professionalId);
            return Results.Ok(new { connectUrl = authUrl, expiresInSeconds = 600 });
        });

        // ─── GET /api/payments/mp/callback ────────────────────────────────────────
        // Public — called by MP after user authorizes. Validates state, exchanges code, persists.
        app.MapGet("/api/payments/mp/callback", async (
            string? code,
            string? state,
            string? error,
            HttpContext context,
            IProfessionalMpAccountRepository repo,
            IMpOAuthService mpService,
            IMemoryCache cache,
            IConfiguration config,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("MpOAuthCallback");
            var frontendBase = config["MercadoPago__FrontendBaseUrl"]
                ?? config["APP_BASE_URL"]
                ?? "https://jobeasy.com.br";

            // ── Rate limiting per IP ───────────────────────────────────────────────
            // Note: in Lambda each instance tracks its own counter (no shared state).
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var rlKey = $"mp:rl:callback:{ip}";
            var count = cache.GetOrCreate(rlKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CallbackRateLimitWindow;
                return 0;
            });
            cache.Set(rlKey, count + 1, CallbackRateLimitWindow);
            if (count >= CallbackRateLimitMax)
            {
                logger.LogWarning("[MpCallback] Rate limit exceeded for IP {Ip}", ip);
                return Results.Json(new { error = "rate_limit_exceeded" }, statusCode: 429);
            }

            // ── MP denied the authorization (user cancelled) ───────────────────────
            if (!string.IsNullOrWhiteSpace(error))
            {
                logger.LogInformation("[MpCallback] Authorization denied by user. Error={Error}", error);
                return Results.Redirect($"{frontendBase}/profissional?mp_error=access_denied");
            }

            // ── Validate anti-CSRF state ───────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(state))
                return Results.Redirect($"{frontendBase}/profissional?mp_error=invalid_state");

            var professionalId = mpService.ValidateAndConsumeState(state);
            if (professionalId is null)
            {
                logger.LogWarning("[MpCallback] Invalid or expired state token");
                return Results.Redirect($"{frontendBase}/profissional?mp_error=invalid_state");
            }

            if (string.IsNullOrWhiteSpace(code))
                return Results.Redirect($"{frontendBase}/profissional?mp_error=code_missing");

            // ── Exchange code for tokens ───────────────────────────────────────────
            MpTokenResponse tokenResponse;
            try
            {
                tokenResponse = await mpService.ExchangeCodeAsync(code, ct);
            }
            catch (MpOAuthException ex)
            {
                logger.LogError(
                    "[MpCallback] Code exchange failed for professional {ProfessionalId}. Status={Status}",
                    professionalId, ex.StatusCode);
                return Results.Redirect($"{frontendBase}/profissional?mp_error=token_exchange_failed");
            }

            // ── Anti-hijack: if an account already exists, MpUserId must match ─────
            var existing = await repo.GetByProfessionalIdAsync(professionalId, ct);
            if (existing is not null && existing.MpUserId != tokenResponse.UserId)
            {
                logger.LogError(
                    "[MpCallback] MpUserId mismatch for professional {ProfessionalId}. " +
                    "Existing={ExistingId} New={NewId}",
                    professionalId, existing.MpUserId, tokenResponse.UserId);
                return Results.Redirect($"{frontendBase}/profissional?mp_error=mp_user_mismatch");
            }

            // ── Upsert account ─────────────────────────────────────────────────────
            var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            await repo.UpsertAsync(
                professionalId,
                tokenResponse.UserId,
                tokenResponse.AccessToken,
                tokenResponse.RefreshToken,
                expiresAt,
                tokenResponse.LiveMode,
                ct);

            logger.LogInformation(
                "[MpCallback] Professional {ProfessionalId} connected MP user {MpUserId}",
                professionalId, tokenResponse.UserId);

            return Results.Redirect($"{frontendBase}/profissional?mp_connected=true");
        });

        // ─── DELETE /api/professionals/{professionalId}/mp/disconnect ─────────────
        // JWT required.
        app.MapDelete("/api/professionals/{professionalId}/mp/disconnect", async (
            string professionalId,
            HttpContext context,
            AppDbContext db,
            IProfessionalMpAccountRepository repo,
            IMpOAuthService mpService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("MpOAuthDisconnect");

            var userId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Json(new { error = "Não autorizado" }, statusCode: 401);

            var owns = await db.Professionals
                .AsNoTracking()
                .AnyAsync(p => p.Id == professionalId && p.UserId == userId, ct);
            if (!owns)
                return Results.Json(new { error = "Profissional não encontrado ou sem permissão" }, statusCode: 403);

            await repo.TryRevokeAndDisconnectAsync(professionalId, mpService, ct);

            logger.LogInformation("[MpDisconnect] Professional {ProfessionalId} disconnected MP", professionalId);
            return Results.Ok(new { ok = true });
        });

        // ─── GET /api/professionals/{professionalId}/mp/status ───────────────────
        // JWT required. Returns current MP connection status.
        app.MapGet("/api/professionals/{professionalId}/mp/status", async (
            string professionalId,
            HttpContext context,
            IProfessionalMpAccountRepository repo,
            CancellationToken ct) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Json(new { error = "Não autorizado" }, statusCode: 401);

            var account = await repo.GetByProfessionalIdAsync(professionalId, ct);
            if (account is null)
                return Results.Ok(new { connected = false });

            var isExpiringSoon = account.MpTokenExpiresAt <= DateTime.UtcNow.AddDays(7);

            return Results.Ok(new
            {
                connected = account.Status == "active",
                status = account.Status,
                mpUserId = account.MpUserId.ToString(),
                liveMode = account.LiveMode,
                tokenExpiresAt = account.MpTokenExpiresAt,
                connectedAt = account.CreatedAt,
                isExpiringSoon,
            });
        });

        // ─── POST /internal/jobs/mp-token-refresh ────────────────────────────────
        // EventBridge / Lambda trigger for the token refresh sweep.
        app.MapPost("/internal/jobs/mp-token-refresh", async (
            MpTokenRefreshJob job,
            CancellationToken ct) =>
        {
            await job.RunAsync(ct);
            return Results.Ok(new { ok = true, job = "mp-token-refresh" });
        });

        return app;
    }
}
