using Api.Security;
using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Api.Extensions;

public static class MpOAuthEndpoints
{
    public static IEndpointRouteBuilder MapMpOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── GET /professionals/{professionalId}/mp/connect-url ────────────────
        app.MapGet("/professionals/{professionalId}/mp/connect-url", async (
            string professionalId,
            HttpContext context,
            IMpOAuthService mpService,
            AppDbContext ctx,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("MpConnect");

            var jwtUserId = AuthorizationHelpers.GetJwtUserId(context);
            if (jwtUserId is null)
                return Results.Json(new { error = "Autenticação necessária" }, statusCode: 401);

            var professional = await ctx.Professionals
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == professionalId, ct);

            if (professional is null)
                return Results.Json(new { error = "Profissional não encontrado" }, statusCode: 404);

            // ✅ BUG 3 CORRIGIDO: IsOwnerOrAdmin com fallback para sessões legadas.
            //
            // Cenário original de falha:
            //   O JWT tem sub = User.Id, mas para sessões antigas o sub pode ser
            //   o professional.Id em vez do user.Id. O check original só comparava
            //   professional.UserId == jwtUserId, causando 403 nesses casos.
            //
            // Correção: aceita também professional.Id == jwtUserId como ownership
            // válida (backwards-compat). Admin sempre passa.
            if (!AuthorizationHelpers.IsOwnerOrAdmin(context, professional))
            {
                logger.LogWarning(
                    "[MpConnect] 403 Acesso negado. " +
                    "ProfessionalId={ProfessionalId} " +
                    "Professional.UserId={ProfUserId} " +
                    "JWT.Sub={JwtSub} " +
                    "Professional.Id={ProfId}",
                    professionalId,
                    professional.UserId ?? "(null)",
                    jwtUserId ?? "(null)",
                    professional.Id);

                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);
            }

            string connectUrl;
            int expiresInSeconds;
            try
            {
                (connectUrl, expiresInSeconds) = await mpService.GetConnectUrlAsync(professionalId, ct);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("não configurado"))
            {
                logger.LogError("[MpConnect] Credencial MP ausente: {Message}", ex.Message);
                return Results.Json(
                    new { error = "Integração com Mercado Pago não está configurada. Contate o suporte." },
                    statusCode: 503);
            }

            logger.LogInformation(
                "[MpConnect] URL gerada para ProfessionalId={ProfessionalId}. ExpiresIn={Expires}s",
                professionalId, expiresInSeconds);

            // ⚠️  Retorna `connectUrl` (nome que o frontend lê após a correção do Bug 2).
            //     Não renomear para `url` sem atualizar useMpConnect.ts em sincronia.
            return Results.Ok(new { connectUrl, expiresInSeconds });
        });

        // ─── GET /payments/mp/callback ─────────────────────────────────────────
        app.MapGet("/payments/mp/callback", async (
            string? code,
            string? state,
            string? error,
            HttpContext context,
            IMpOAuthService mpService,
            IProfessionalMpAccountRepository mpRepo,
            AppDbContext ctx,
            IConfiguration config,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("MpCallback");
            // ✅ correto
            var frontendBase = config["MercadoPago:FrontendBaseUrl"] 
                ?? config["MercadoPago__FrontendBaseUrl"] 
                ?? "";

            // Validate state anti-CSRF
            if (string.IsNullOrWhiteSpace(state))
                return Results.Redirect($"{frontendBase}/profissional?mp_error=invalid_state");

            var (professionalId, codeVerifier) = await mpService.ValidateAndConsumeStateAsync(state, ct);
            if (professionalId is null)
                return Results.Redirect($"{frontendBase}/profissional?mp_error=invalid_state");

            // MP reported an error during authorization
            if (!string.IsNullOrWhiteSpace(error))
            {
                logger.LogWarning("[MpCallback] MP returned error for professional {ProfessionalId}: {Error}",
                    professionalId, error);
                return Results.Redirect($"{frontendBase}/profissional?mp_error={Uri.EscapeDataString(error)}");
            }

            if (string.IsNullOrWhiteSpace(code))
                return Results.Redirect($"{frontendBase}/profissional?mp_error=missing_code");

            // Exchange authorization code for tokens
            MpTokenResponse tokenResponse;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                tokenResponse = await mpService.ExchangeCodeForTokensAsync(code, codeVerifier, cts.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("[MpCallback] Timeout exchanging code for professional {ProfessionalId}",
                    professionalId);
                return Results.Redirect($"{frontendBase}/profissional?mp_error=timeout");
            }
            catch (MpOAuthException ex)
            {
                logger.LogError(ex, "[MpCallback] MP error for professional {ProfessionalId}", professionalId);
                return Results.Redirect(
                    $"{frontendBase}/profissional?mp_error=mp_error&detail={Uri.EscapeDataString(ex.Message)}");
            }

            // Anti-hijack: verify MP user ID matches existing account if one exists
            var existing = await mpRepo.GetByProfessionalIdAsync(professionalId, ct);
            if (existing is not null && existing.MpUserId != tokenResponse.UserId.ToString())
            {
                logger.LogWarning(
                    "[MpCallback] MP user_id mismatch for professional {ProfessionalId}. " +
                    "Expected={Expected} Got={Got}",
                    professionalId, existing.MpUserId, tokenResponse.UserId);
                return Results.Redirect($"{frontendBase}/profissional?mp_error=user_mismatch");
            }

            var professional = await ctx.Professionals
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == professionalId, ct);

            if (professional is null)
            {
                logger.LogWarning("[MpCallback] Professional not found: {ProfessionalId}", professionalId);
                return Results.Redirect($"{frontendBase}/profissional?mp_error=not_found");
            }

            var now = DateTime.UtcNow;
            var expiresAt = now.AddSeconds(tokenResponse.ExpiresIn);

            var account = new ProfessionalMpAccount(
                Id: existing?.Id ?? Guid.NewGuid(),
                ProfessionalId: professionalId,
                MpUserId: tokenResponse.UserId.ToString(),
                MpAccessToken: tokenResponse.AccessToken,
                MpRefreshToken: tokenResponse.RefreshToken,
                MpTokenExpiresAt: expiresAt,
                MpScope: tokenResponse.Scope,
                MpLiveMode: tokenResponse.LiveMode,
                Status: "active",
                ConnectedAt: existing?.ConnectedAt ?? now,
                LastRefreshedAt: existing is null ? null : now,
                CreatedAt: existing?.CreatedAt ?? now,
                UpdatedAt: now);

            await mpRepo.UpsertAsync(account, ct);

            await ctx.Professionals
                .Where(p => p.Id == professionalId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.MpConnected, true)
                    .SetProperty(p => p.MpConnectedAt, now),
                ct);

            logger.LogInformation(
                "[MpCallback] Professional {ProfessionalId} connected MP account " +
                "(user_id={MpUserId}, live={Live})",
                professionalId, tokenResponse.UserId, tokenResponse.LiveMode);

            return Results.Redirect($"{frontendBase}/profissional?mp_connected=true");
        }).RequireRateLimiting("mp-callback");

        // ─── DELETE /professionals/{professionalId}/mp/disconnect ──────────────
        app.MapDelete("/professionals/{professionalId}/mp/disconnect", async (
            string professionalId,
            HttpContext context,
            IProfessionalMpAccountRepository mpRepo,
            AppDbContext ctx,
            CancellationToken ct) =>
        {
            var jwtUserId = AuthorizationHelpers.GetJwtUserId(context);
            if (jwtUserId is null)
                return Results.Json(new { error = "Autenticação necessária" }, statusCode: 401);

            var professional = await ctx.Professionals
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == professionalId, ct);

            if (professional is null)
                return Results.Json(new { error = "Profissional não encontrado" }, statusCode: 404);

            if (!AuthorizationHelpers.IsOwnerOrAdmin(context, professional))
                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

            await mpRepo.UpdateStatusAsync(professionalId, "revoked", ct);
            await ctx.Professionals
                .Where(p => p.Id == professionalId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.MpConnected, false), ct);

            return Results.Ok(new { ok = true });
        });

        // ─── GET /professionals/{professionalId}/mp/status ─────────────────────
        app.MapGet("/professionals/{professionalId}/mp/status", async (
            string professionalId,
            HttpContext context,
            IProfessionalMpAccountRepository mpRepo,
            AppDbContext ctx,
            CancellationToken ct) =>
        {
            var jwtUserId = AuthorizationHelpers.GetJwtUserId(context);
            if (jwtUserId is null)
                return Results.Json(new { error = "Autenticação necessária" }, statusCode: 401);

            var professional = await ctx.Professionals
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == professionalId, ct);

            if (professional is null)
                return Results.Json(new { error = "Profissional não encontrado" }, statusCode: 404);

            if (!AuthorizationHelpers.IsOwnerOrAdmin(context, professional))
                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

            var account = await mpRepo.GetByProfessionalIdAsync(professionalId, ct);

            if (account is null || account.Status == "revoked")
                return Results.Ok(new { connected = false, status = "disconnected" });

            var now = DateTime.UtcNow;
            var isExpiringSoon = account.MpTokenExpiresAt < now.AddDays(7);

            var connectionStatus = isExpiringSoon ? "expiring_soon" : "connected";

            return Results.Ok(new
            {
                connected = true,
                status = connectionStatus,
                mpUserId = account.MpUserId,
                connectedAt = account.ConnectedAt,
                tokenExpiresAt = account.MpTokenExpiresAt,
                isExpiringSoon,
                liveMode = account.MpLiveMode,
            });
        });

        return app;
    }
}
