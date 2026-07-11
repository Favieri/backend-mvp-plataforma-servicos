using Api.Security;
using Application.Abstractions;
using Application.DTOs;
using FluentValidation;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Extensions;

public static class VerificationEndpoints
{
    public static IEndpointRouteBuilder MapVerificationEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── GET /professionals/{id}/verification ─────────────────────────────
        // Retorna o registro de verificação mais recente do profissional.
        app.MapGet("/professionals/{id}/verification", async (
            string id,
            IProfessionalVerificationRepository repo,
            CancellationToken ct) =>
        {
            var verification = await repo.GetLatestByProfessionalIdAsync(id, ct);
            return verification is null
                ? Results.NotFound(new { error = "Nenhum documento de verificação encontrado." })
                : Results.Ok(verification);
        });

        // ─── GET /professionals/{id}/verification/history ─────────────────────
        // Retorna o histórico completo de tentativas de verificação.
        app.MapGet("/professionals/{id}/verification/history", async (
            string id,
            IProfessionalVerificationRepository repo,
            CancellationToken ct) =>
        {
            var history = await repo.GetHistoryByProfessionalIdAsync(id, ct);
            return Results.Ok(history);
        });

        // ─── POST /professionals/{id}/verification ────────────────────────────
        // Profissional envia documento para verificação.
        app.MapPost("/professionals/{id}/verification", async (
            string id,
            SubmitVerificationRequest body,
            HttpContext context,
            IProfessionalVerificationRepository repo,
            AppDbContext dbCtx,
            CancellationToken ct) =>
        {
            var professional = await dbCtx.Professionals.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (professional is null)
                return Results.Json(new { error = "Profissional não encontrado." }, statusCode: 404);

            if (!AuthorizationHelpers.IsOwnerOrAdmin(context, professional))
                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

            if (string.IsNullOrWhiteSpace(body.DocumentType))
                return Results.Json(new { error = "documentType é obrigatório." }, statusCode: 400);

            if (string.IsNullOrWhiteSpace(body.DocumentUrl))
                return Results.Json(new { error = "documentUrl é obrigatório." }, statusCode: 400);

            var validDocumentTypes = new[] { "rg", "cnh", "cpf", "cnpj", "diploma", "crea", "cau", "crm", "oab", "other" };
            if (!validDocumentTypes.Contains(body.DocumentType.ToLowerInvariant()))
                return Results.Json(new { error = $"documentType inválido. Valores aceitos: {string.Join(", ", validDocumentTypes)}." }, statusCode: 400);

            var result = await repo.SubmitAsync(id, body.DocumentType.ToLowerInvariant(), body.DocumentUrl, ct);
            return Results.Json(result, statusCode: 201);
        });

        // ─── PUT /professionals/verification/{verificationId}/review ──────────
        // Admin atualiza o status de verificação de um documento.
        app.MapPut("/professionals/verification/{verificationId}/review", async (
            string verificationId,
            ReviewVerificationRequest body,
            HttpContext context,
            IProfessionalVerificationRepository repo,
            CancellationToken ct) =>
        {
            if (AuthorizationHelpers.RequireAdmin(context) is { } authError)
                return authError;

            var validStatuses = new[] { "in_review", "verified", "rejected" };
            if (string.IsNullOrWhiteSpace(body.Status) || !validStatuses.Contains(body.Status))
                return Results.Json(new { error = $"status inválido. Valores aceitos: {string.Join(", ", validStatuses)}." }, statusCode: 400);

            if (body.Status == "rejected" && string.IsNullOrWhiteSpace(body.Notes))
                return Results.Json(new { error = "notes é obrigatório ao rejeitar um documento." }, statusCode: 400);

            var reviewedBy = AuthorizationHelpers.GetJwtUserId(context)!;

            var result = await repo.ReviewAsync(verificationId, body.Status, body.Notes, reviewedBy, ct);
            return result is null
                ? Results.NotFound(new { error = "Verificação não encontrada." })
                : Results.Ok(result);
        });

        // ─── GET /admin/verification/pending ─────────────────────────────────
        // Fila de documentos aguardando revisão (admin).
        app.MapGet("/admin/verification/pending", async (
            HttpContext context,
            IProfessionalVerificationRepository repo,
            CancellationToken ct) =>
        {
            if (AuthorizationHelpers.RequireAdmin(context) is { } authError)
                return authError;

            var pending = await repo.GetPendingReviewAsync(ct);
            return Results.Ok(pending);
        });

        // ─── GET /professionals/{id}/trust-metrics ────────────────────────────
        // Retorna as métricas de confiança calculadas de um profissional.
        app.MapGet("/professionals/{id}/trust-metrics", async (
            string id,
            Application.Abstractions.IProfessionalDetailRepository repo,
            CancellationToken ct) =>
        {
            var metrics = await repo.GetTrustMetricsAsync(id, ct);
            if (metrics is null)
                return Results.NotFound(new { error = "Profissional não encontrado." });

            return Results.Ok(metrics);
        });

        // ─── POST /internal/jobs/trust-metrics ───────────────────────────────
        // Dispara recálculo de métricas para todos os profissionais.
        // Pode ser chamado por EventBridge ou manualmente.
        app.MapPost("/internal/jobs/trust-metrics", async (
            string? professionalId,
            ITrustMetricsService service,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(professionalId))
            {
                await service.RecalculateAsync(professionalId, ct);
                return Results.Ok(new { ok = true, scope = "single", professionalId });
            }

            _ = Task.Run(() => service.RecalculateAllAsync(CancellationToken.None));
            return Results.Accepted(value: new { ok = true, scope = "all", message = "Recálculo iniciado em background." });
        });

        return app;
    }
}
