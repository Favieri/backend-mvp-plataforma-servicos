using Application.Abstractions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Api.Extensions;

public static class WalletEndpoints
{
    public static IEndpointRouteBuilder MapWalletEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── GET /api/wallet/balance ───────────────────────────────────────────
        app.MapGet("/api/wallet/balance", async (
            HttpContext context,
            string? professionalId,
            ILedgerRepository ledgerRepo,
            AppDbContext ctx,
            CancellationToken ct) =>
        {
            var (resolvedId, error) = await ResolveProfessionalIdAsync(context, professionalId, ctx, ct);
            if (error is not null)
                return error;

            var balance = await ledgerRepo.GetBalanceAsync(resolvedId!, ct);
            return Results.Ok(balance);
        });

        // ─── GET /api/wallet/ledger ────────────────────────────────────────────
        app.MapGet("/api/wallet/ledger", async (
            HttpContext context,
            string? professionalId,
            int? page,
            int? pageSize,
            string? from,
            string? to,
            string? type,
            ILedgerRepository ledgerRepo,
            AppDbContext ctx,
            CancellationToken ct) =>
        {
            var (resolvedId, error) = await ResolveProfessionalIdAsync(context, professionalId, ctx, ct);
            if (error is not null)
                return error;

            var p    = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 20, 1, 50);

            var fromDt = DateTime.TryParse(from, out var fp) ? fp : (DateTime?)null;
            var toDt   = DateTime.TryParse(to,   out var tp) ? tp : (DateTime?)null;

            var (items, total) = await ledgerRepo.GetLedgerAsync(resolvedId!, p, size, fromDt, toDt, type, ct);

            return Results.Ok(new
            {
                items,
                total,
                page        = p,
                pageSize    = size,
                totalPages  = (int)Math.Ceiling(total / (double)size),
            });
        });

        // ─── GET /api/wallet/summary ───────────────────────────────────────────
        app.MapGet("/api/wallet/summary", async (
            HttpContext context,
            string? professionalId,
            int? months,
            ILedgerRepository ledgerRepo,
            AppDbContext ctx,
            CancellationToken ct) =>
        {
            var (resolvedId, error) = await ResolveProfessionalIdAsync(context, professionalId, ctx, ct);
            if (error is not null)
                return error;

            var m = Math.Clamp(months ?? 6, 1, 12);
            var summaries = await ledgerRepo.GetMonthlySummaryAsync(resolvedId!, m, ct);
            return Results.Ok(new { months = summaries });
        });

        return app;
    }

    // ─── Helper: resolves professional ID from JWT + optional admin override ──
    private static async Task<(string? Id, IResult? Error)> ResolveProfessionalIdAsync(
        HttpContext context,
        string? adminOverride,
        AppDbContext ctx,
        CancellationToken ct)
    {
        var userId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            return (null, Results.Json(new { error = "Autenticação obrigatória" }, statusCode: 401));

        var role = context.User.FindFirst("role")?.Value ?? string.Empty;

        // Admin can query any professional via ?professionalId=
        if (role == "admin" && !string.IsNullOrWhiteSpace(adminOverride))
            return (adminOverride, null);

        // Look up the professional record linked to the authenticated user
        var professionalId = await ctx.Professionals
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(ct);

        if (professionalId is null)
            return (null, Results.Json(new { error = "Profissional não encontrado para este usuário" }, statusCode: 404));

        return (professionalId, null);
    }
}
