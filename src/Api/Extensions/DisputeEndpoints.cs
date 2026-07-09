using Api.Security;
using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Api.Extensions;

public static class DisputeEndpoints
{
    private static async Task<bool> IsDisputePartyOrAdminAsync(
        HttpContext context, Dispute dispute, AppDbContext ctx, CancellationToken ct)
    {
        if (AuthorizationHelpers.IsAdmin(context))
            return true;

        var jwtUserId = AuthorizationHelpers.GetJwtUserId(context);
        if (string.IsNullOrWhiteSpace(jwtUserId))
            return false;

        if (dispute.ClientId == jwtUserId)
            return true;

        return await ctx.Professionals.AsNoTracking()
            .AnyAsync(p => p.Id == dispute.ProfessionalId && (p.UserId == jwtUserId || p.Id == jwtUserId), ct);
    }

    public static IEndpointRouteBuilder MapDisputeEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /disputes — open a dispute (client)
        app.MapPost("/disputes", async (
            OpenDisputeRequest body, HttpContext context, IDisputeRepository repo, IOrderRepository orderRepo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.OrderId))
                return Results.Json(new { error = "orderId é obrigatório" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.Reason))
                return Results.Json(new { error = "reason é obrigatório" }, statusCode: 400);

            var jwtUserId = AuthorizationHelpers.GetJwtUserId(context);
            if (string.IsNullOrWhiteSpace(jwtUserId))
                return Results.Json(new { error = "Autenticação necessária" }, statusCode: 401);

            // Ensure order exists and belongs to the authenticated client
            var order = await orderRepo.GetByIdAsync(body.OrderId, ct);
            if (order is null)
                return Results.Json(new { error = "Pedido não encontrado" }, statusCode: 404);
            if (order.ClientId != jwtUserId)
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
                clientId: jwtUserId,
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
        app.MapPut("/disputes/{id}/respond", async (
            string id, RespondDisputeRequest body, HttpContext context, IDisputeRepository repo, AppDbContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Response))
                return Results.Json(new { error = "response é obrigatório" }, statusCode: 400);

            var dispute = await repo.GetByIdAsync(id, ct);
            if (dispute is null)
                return Results.NotFound(new { error = "Disputa não encontrada" });

            var professional = await ctx.Professionals.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == dispute.ProfessionalId, ct);
            if (professional is null || !AuthorizationHelpers.IsOwnerOrAdmin(context, professional))
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
        app.MapPut("/disputes/{id}/resolve", async (
            string id,
            ResolveDisputeRequest body,
            HttpContext context,
            IDisputeRepository repo,
            IPaymentRepository paymentRepo,
            ILedgerRepository ledgerRepo,
            IRefundService refundService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("DisputeEndpoints");

            var adminError = AuthorizationHelpers.RequireAdmin(context);
            if (adminError is not null)
                return adminError;

            if (string.IsNullOrWhiteSpace(body.Resolution))
                return Results.Json(new { error = "resolution é obrigatório" }, statusCode: 400);

            var resolvedBy = AuthorizationHelpers.GetJwtUserId(context)!;

            var dispute = await repo.GetByIdAsync(id, ct);
            if (dispute is null)
                return Results.NotFound(new { error = "Disputa não encontrada" });
            if (Domain.Enums.DisputeStatus.Terminal.Contains(dispute.Status))
                return Results.Json(new { error = "Disputa já encerrada" }, statusCode: 422);

            await repo.ResolveAsync(id, body.Resolution.Trim(), resolvedBy, body.RefundAmountCents, ct);

            // Disputa resolvida a favor do cliente: disparar reembolso
            if (body.RefundAmountCents > 0)
            {
                try
                {
                    var refundResult = await refundService.RefundOrderAsync(
                        orderId: dispute.OrderId,
                        reason: "dispute_resolved_client",
                        amountCents: body.RefundAmountCents,
                        ct);

                    if (refundResult.Success)
                    {
                        // Ledger: débito para o profissional
                        await ledgerRepo.AddAsync(LedgerEntry.Create(
                            type: "earning_dispute_refunded",
                            orderId: dispute.OrderId,
                            paymentId: null,
                            professionalId: dispute.ProfessionalId,
                            amountCents: -body.RefundAmountCents.Value), ct);
                        // Nota: RefundService já gerencia Order.status:
                        //   reembolso total → status='refunded'
                        //   reembolso parcial → status permanece inalterado
                    }
                    else
                    {
                        logger.LogError(
                            "[DisputeEndpoints] Falha ao reembolsar disputa {DisputeId}. ErrorCode={Code}",
                            id, refundResult.ErrorCode);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[DisputeEndpoints] Exceção ao reembolsar disputa {DisputeId}", id);
                }
            }
            else
            {
                // Disputa resolvida a favor do profissional: liberar earning congelado
                var payment = await paymentRepo.GetPaidByOrderIdAsync(dispute.OrderId, ct);
                if (payment is not null && dispute.ProfessionalId is not null)
                {
                    var netCents = payment.AmountCents - payment.PlatformFeeCents - payment.GatewayFeeCents;
                    if (netCents > 0)
                    {
                        await ledgerRepo.AddAsync(LedgerEntry.Create(
                            type: "earning_dispute_released",
                            orderId: dispute.OrderId,
                            paymentId: payment.Id,
                            professionalId: dispute.ProfessionalId,
                            amountCents: netCents), ct);
                    }
                }
            }

            return Results.Ok(new { ok = true, id, status = Domain.Enums.DisputeStatus.Resolved });
        });

        // PUT /disputes/{id}/escalate — escalate to mediation
        app.MapPut("/disputes/{id}/escalate", async (
            string id, HttpContext context, IDisputeRepository repo, AppDbContext ctx, CancellationToken ct) =>
        {
            var dispute = await repo.GetByIdAsync(id, ct);
            if (dispute is null)
                return Results.NotFound(new { error = "Disputa não encontrada" });
            if (!await IsDisputePartyOrAdminAsync(context, dispute, ctx, ct))
                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);
            if (dispute.Status != Domain.Enums.DisputeStatus.ProfessionalResponded)
                return Results.Json(new { error = $"Disputa no status '{dispute.Status}' não pode ser escalada" }, statusCode: 422);

            await repo.EscalateAsync(id, ct);
            return Results.Ok(new { ok = true, id, status = Domain.Enums.DisputeStatus.Mediating });
        });

        // GET /disputes/{id} — get dispute details
        app.MapGet("/disputes/{id}", async (
            string id, HttpContext context, IDisputeRepository repo, AppDbContext ctx, CancellationToken ct) =>
        {
            var dispute = await repo.GetByIdAsync(id, ct);
            if (dispute is null)
                return Results.NotFound(new { error = "Disputa não encontrada" });
            if (!await IsDisputePartyOrAdminAsync(context, dispute, ctx, ct))
                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

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
        app.MapGet("/disputes", async (
            string? professionalId, string? clientId, HttpContext context, IDisputeRepository repo, AppDbContext ctx, CancellationToken ct) =>
        {
            var jwtUserId = AuthorizationHelpers.GetJwtUserId(context);
            if (string.IsNullOrWhiteSpace(jwtUserId))
                return Results.Json(new { error = "Autenticação necessária" }, statusCode: 401);

            var isAdmin = AuthorizationHelpers.IsAdmin(context);

            if (!string.IsNullOrWhiteSpace(professionalId))
            {
                if (!isAdmin)
                {
                    var (resolvedId, error) = await AuthorizationHelpers.ResolveProfessionalIdAsync(context, professionalId, ctx, ct);
                    if (error is not null)
                        return error;
                    professionalId = resolvedId;
                }
                return Results.Ok(await repo.GetByProfessionalAsync(professionalId!, ct));
            }

            if (!string.IsNullOrWhiteSpace(clientId))
            {
                if (!isAdmin)
                    clientId = jwtUserId;
                return Results.Ok(await repo.GetByClientAsync(clientId, ct));
            }

            return Results.Json(new { error = "professionalId ou clientId é obrigatório" }, statusCode: 400);
        });

        return app;
    }
}
