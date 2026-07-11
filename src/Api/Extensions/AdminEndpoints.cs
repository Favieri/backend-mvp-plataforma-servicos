using Api.Security;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Extensions;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── GET /admin/webhooks/stuck ──────────────────────────────────────────
        // Eventos de webhook não processados (status received/failed) há mais de
        // `minutesThreshold` minutos, para reprocessamento manual pelo painel.
        app.MapGet("/admin/webhooks/stuck", async (
            int? minutesThreshold,
            HttpContext context,
            AppDbContext ctx,
            CancellationToken ct) =>
        {
            if (AuthorizationHelpers.RequireAdmin(context) is { } authError)
                return authError;

            var threshold = DateTime.UtcNow.AddMinutes(-(minutesThreshold ?? 15));

            var stuck = await ctx.WebhookEvents
                .AsNoTracking()
                .Where(w => (w.Status == "failed" || w.Status == "received") && w.CreatedAt < threshold)
                .OrderBy(w => w.CreatedAt)
                .Select(w => new
                {
                    provider = w.Provider,
                    eventId = w.EventId,
                    status = w.Status,
                    createdAt = w.CreatedAt,
                    errorMessage = w.ErrorMessage
                })
                .ToListAsync(ct);

            return Results.Ok(stuck);
        });

        return app;
    }
}
