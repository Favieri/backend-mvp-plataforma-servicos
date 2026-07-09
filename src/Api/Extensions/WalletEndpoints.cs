using Api.Security;
using Application.Abstractions;
using Infrastructure.Persistence;

namespace Api.Extensions;

public static class WalletEndpoints
{
    public static IEndpointRouteBuilder MapWalletEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── GET /wallet/balance ──────────────────────────────────────────────
        app.MapGet("/wallet/balance", async (
            HttpContext context,
            string? professionalId,
            ILedgerRepository ledgerRepo,
            AppDbContext ctx,
            CancellationToken ct) =>
        {
            var (resolvedId, error) = await AuthorizationHelpers.ResolveProfessionalIdAsync(context, professionalId, ctx, ct);
            if (error is not null)
                return error;

            var balance = await ledgerRepo.GetBalanceAsync(resolvedId!, ct);
            return Results.Ok(balance);
        });

        // ─── GET /wallet/ledger ───────────────────────────────────────────────
        app.MapGet("/wallet/ledger", async (
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
            var (resolvedId, error) = await AuthorizationHelpers.ResolveProfessionalIdAsync(context, professionalId, ctx, ct);
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

        // ─── GET /wallet/summary ──────────────────────────────────────────────
        app.MapGet("/wallet/summary", async (
            HttpContext context,
            string? professionalId,
            int? months,
            ILedgerRepository ledgerRepo,
            AppDbContext ctx,
            CancellationToken ct) =>
        {
            var (resolvedId, error) = await AuthorizationHelpers.ResolveProfessionalIdAsync(context, professionalId, ctx, ct);
            if (error is not null)
                return error;

            var m = Math.Clamp(months ?? 6, 1, 12);
            var summaries = await ledgerRepo.GetMonthlySummaryAsync(resolvedId!, m, ct);
            return Results.Ok(new { months = summaries });
        });

        return app;
    }
}
