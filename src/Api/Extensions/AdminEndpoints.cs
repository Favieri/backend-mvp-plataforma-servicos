using Api.Security;
using Application.Abstractions;
using Application.DTOs;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Api.Extensions;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── POST /internal/admin/bootstrap ────────────────────────────────────
        // Cria o primeiro admin da plataforma. Protegido por um segredo separado
        // (ADMIN_BOOTSTRAP_SECRET), não pelo JWT normal. Só funciona enquanto não
        // existir nenhum admin no banco — depois disso o endpoint se autodesliga
        // permanentemente e admins seguintes são criados via POST /users por um
        // admin já autenticado.
        app.MapPost("/internal/admin/bootstrap", async (
            BootstrapAdminRequest body,
            HttpContext context,
            IConfiguration config,
            IUserRepository repo,
            CancellationToken ct) =>
        {
            var expectedSecret = config["ADMIN_BOOTSTRAP_SECRET"];
            if (string.IsNullOrWhiteSpace(expectedSecret))
                return Results.Json(new { error = "Bootstrap de admin não está habilitado." }, statusCode: 403);

            var providedSecret = context.Request.Headers["X-Admin-Bootstrap-Secret"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(providedSecret) || providedSecret != expectedSecret)
                return Results.Json(new { error = "Segredo de bootstrap inválido." }, statusCode: 403);

            if (await repo.AnyAdminExistsAsync(ct))
                return Results.Json(new { error = "Já existe um admin cadastrado. Use POST /users autenticado como admin." }, statusCode: 403);

            var name = body.Name?.Trim() ?? "";
            var email = body.Email?.Trim() ?? "";
            var senha = body.Senha ?? "";

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(senha))
                return Results.Json(new { error = "name, email e senha são obrigatórios" }, statusCode: 400);
            if (await repo.EmailExistsAsync(email, ct))
                return Results.Json(new { error = "Já existe um usuário com este email" }, statusCode: 400);

            var hashed = BCrypt.Net.BCrypt.HashPassword(senha, workFactor: 10);
            var user = await repo.CreateAsync(name, email, body.Phone?.Trim(), "admin", hashed, null, ct);
            return Results.Json(user, statusCode: 201);
        });

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
