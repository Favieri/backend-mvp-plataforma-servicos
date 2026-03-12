using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using System.Text.Json;

namespace Api.Extensions;

public static class DisputeEndpoints
{
    public static IEndpointRouteBuilder MapDisputeEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /disputes — open a dispute (client)
        app.MapPost("/disputes", async (OpenDisputeRequest body, IDisputeRepository repo, IOrderRepository orderRepo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.OrderId))
                return Results.Json(new { error = "orderId é obrigatório" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.ClientId))
                return Results.Json(new { error = "clientId é obrigatório" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.Reason))
                return Results.Json(new { error = "reason é obrigatório" }, statusCode: 400);

            // Ensure order exists and belongs to client
            var order = await orderRepo.GetByIdAsync(body.OrderId, ct);
            if (order is null)
                return Results.Json(new { error = "Pedido não encontrado" }, statusCode: 404);
            if (order.ClientId != body.ClientId)
                return Results.Json(new { error = "Pedido não pertence a este cliente" }, statusCode: 403);

            // Only orders in awaiting_confirmation or disputed-eligible states can be disputed
            var disputeEligibleStatuses = new[]
            {
                Domain.Enums.OrderStatus.AwaitingConfirmation,
                Domain.Enums.OrderStatus.Completed,
                Domain.Enums.OrderStatus.InProgress
            };
            if (!disputeEligibleStatuses.Contains(order.Status))
                return Results.Json(new { error = $"Pedido no status '{order.Status}' não pode ser contestado" }, statusCode: 422);

            // One dispute per order
            if (await repo.OrderHasOpenDisputeAsync(body.OrderId, ct))
                return Results.Json(new { error = "Já existe uma disputa aberta para este pedido" }, statusCode: 409);

            var professionalId = order.ProfessionalId;
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "Pedido não possui profissional associado" }, statusCode: 422);

            var evidenceJson = body.EvidenceUrls?.Length > 0
                ? JsonSerializer.Serialize(body.EvidenceUrls)
                : null;

            var dispute = Dispute.Open(
                id: Guid.NewGuid().ToString(),
                orderId: body.OrderId,
                clientId: body.ClientId,
                professionalId: professionalId,
                reason: body.Reason.Trim(),
                description: body.Description?.Trim(),
                evidenceUrls: evidenceJson);

            await repo.CreateAsync(dispute, ct);

            // Transition order to disputed
            await orderRepo.MarkDisputedAsync(body.OrderId, ct);

            return Results.Json(new
            {
                ok = true,
                id = dispute.Id,
                orderId = dispute.OrderId,
                status = dispute.Status,
                createdAt = dispute.CreatedAt
            }, statusCode: 201);
        });

        // PUT /disputes/{id}/respond — professional responds
        app.MapPut("/disputes/{id}/respond", async (string id, RespondDisputeRequest body, IDisputeRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.Response))
                return Results.Json(new { error = "response é obrigatório" }, statusCode: 400);

            var dispute = await repo.GetByIdAsync(id, ct);
            if (dispute is null)
                return Results.NotFound(new { error = "Disputa não encontrada" });
            if (dispute.ProfessionalId != body.ProfessionalId)
                return Results.Json(new { error = "Não autorizado" }, statusCode: 403);
            if (dispute.Status != Domain.Enums.DisputeStatus.Opened)
                return Results.Json(new { error = $"Disputa no status '{dispute.Status}' não aceita resposta" }, statusCode: 422);

            var evidenceJson = body.EvidenceUrls?.Length > 0
                ? JsonSerializer.Serialize(body.EvidenceUrls)
                : null;

            await repo.AddProfessionalResponseAsync(id, body.Response.Trim(), evidenceJson, ct);

            return Results.Ok(new { ok = true, id, status = Domain.Enums.DisputeStatus.ProfessionalResponded });
        });

        // PUT /disputes/{id}/resolve — resolve (admin/system)
        app.MapPut("/disputes/{id}/resolve", async (string id, ResolveDisputeRequest body, IDisputeRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Resolution))
                return Results.Json(new { error = "resolution é obrigatório" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.ResolvedBy))
                return Results.Json(new { error = "resolvedBy é obrigatório" }, statusCode: 400);

            var dispute = await repo.GetByIdAsync(id, ct);
            if (dispute is null)
                return Results.NotFound(new { error = "Disputa não encontrada" });
            if (Domain.Enums.DisputeStatus.Terminal.Contains(dispute.Status))
                return Results.Json(new { error = "Disputa já encerrada" }, statusCode: 422);

            await repo.ResolveAsync(id, body.Resolution.Trim(), body.ResolvedBy.Trim(), body.RefundAmountCents, ct);

            return Results.Ok(new { ok = true, id, status = Domain.Enums.DisputeStatus.Resolved });
        });

        // PUT /disputes/{id}/escalate — escalate to mediation
        app.MapPut("/disputes/{id}/escalate", async (string id, IDisputeRepository repo, CancellationToken ct) =>
        {
            var dispute = await repo.GetByIdAsync(id, ct);
            if (dispute is null)
                return Results.NotFound(new { error = "Disputa não encontrada" });
            if (dispute.Status != Domain.Enums.DisputeStatus.ProfessionalResponded)
                return Results.Json(new { error = $"Disputa no status '{dispute.Status}' não pode ser escalada" }, statusCode: 422);

            await repo.EscalateAsync(id, ct);
            return Results.Ok(new { ok = true, id, status = Domain.Enums.DisputeStatus.Mediating });
        });

        // GET /disputes/{id} — get dispute details
        app.MapGet("/disputes/{id}", async (string id, IDisputeRepository repo, CancellationToken ct) =>
        {
            var dispute = await repo.GetByIdAsync(id, ct);
            if (dispute is null)
                return Results.NotFound(new { error = "Disputa não encontrada" });

            return Results.Ok(new
            {
                id = dispute.Id,
                orderId = dispute.OrderId,
                clientId = dispute.ClientId,
                professionalId = dispute.ProfessionalId,
                reason = dispute.Reason,
                description = dispute.Description,
                evidenceUrls = dispute.EvidenceUrls,
                professionalResponse = dispute.ProfessionalResponse,
                professionalEvidenceUrls = dispute.ProfessionalEvidenceUrls,
                resolution = dispute.Resolution,
                resolvedBy = dispute.ResolvedBy,
                refundAmountCents = dispute.RefundAmountCents,
                status = dispute.Status,
                createdAt = dispute.CreatedAt,
                resolvedAt = dispute.ResolvedAt
            });
        });

        // GET /disputes — list by professional or client
        app.MapGet("/disputes", async (string? professionalId, string? clientId, IDisputeRepository repo, CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(professionalId))
                return Results.Ok(await repo.GetByProfessionalAsync(professionalId, ct));
            if (!string.IsNullOrWhiteSpace(clientId))
                return Results.Ok(await repo.GetByClientAsync(clientId, ct));
            return Results.Json(new { error = "professionalId ou clientId é obrigatório" }, statusCode: 400);
        });

        return app;
    }
}
