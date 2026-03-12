using Application.Abstractions;
using Application.DTOs;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Api.Extensions;

public static class OrderEndpoints
{
    private const string InternalSecretHeader = "X-Internal-Secret";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── POST /orders/booking — Tier 1 direct booking ─────────────────────
        app.MapPost("/orders/booking", async (
            CreateBookingRequest body,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            IServiceCatalogRepository catalog,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ClientId))
                return Results.Json(new { error = "clientId é obrigatório" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.ProfessionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.ServiceId))
                return Results.Json(new { error = "serviceId é obrigatório" }, statusCode: 400);
            if (body.PriceTotalCents <= 0)
                return Results.Json(new { error = "priceTotalCents deve ser positivo" }, statusCode: 400);

            // Validate tier allows direct booking
            var tiers = await catalog.GetTiersAsync(ct);
            var tier = tiers.FirstOrDefault(t => t.Id == body.TierId);
            if (tier is null)
                return Results.Json(new { error = "tierId inválido" }, statusCode: 400);
            if (!tier.AllowBookingDirect)
                return Results.Json(new { error = "Este tier não permite booking direto. Use proposta." }, statusCode: 422);

            var scheduledAt = DateTime.TryParse(body.ScheduledAt, out var parsed) ? parsed : (DateTime?)null;
            var order = Order.CreateBooking(
                id: Guid.NewGuid().ToString(),
                clientId: body.ClientId,
                professionalId: body.ProfessionalId,
                serviceId: body.ServiceId,
                tierId: body.TierId,
                priceTotalCents: body.PriceTotalCents,
                signalCents: body.SignalCents,
                balanceCents: body.BalanceCents,
                installments: body.Installments > 0 ? body.Installments : 1,
                paymentMethod: body.PaymentMethod,
                scope: body.Scope,
                scheduledAt: scheduledAt,
                conversationId: body.ConversationId,
                addressId: body.AddressId,
                description: body.Description);

            var created = await orderRepo.CreateBookingAsync(order, ct);

            await timeline.AddEventAsync(OrderTimeline.Create(
                id: Guid.NewGuid().ToString(),
                orderId: created.Id,
                eventType: "order_created",
                actorId: body.ClientId,
                actorRole: ActorRole.Client,
                metadata: $"{{\"origin\":\"booking_direct\",\"tierId\":{body.TierId}}}"), ct);

            return Results.Json(created, statusCode: 201);
        });

        // ─── POST /orders/from-proposal/{proposalId} — Tier 2/3 ──────────────
        app.MapPost("/orders/from-proposal/{proposalId}", async (
            string proposalId,
            CreateFromProposalRequest body,
            IOrderRepository orderRepo,
            IProposalRepository proposalRepo,
            IOrderTimelineRepository timeline,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ClientId))
                return Results.Json(new { error = "clientId é obrigatório" }, statusCode: 400);

            var proposal = await proposalRepo.GetByIdAsync(proposalId, ct);
            if (proposal is null)
                return Results.Json(new { error = "Proposta não encontrada" }, statusCode: 404);
            if (proposal.ClientId != body.ClientId)
                return Results.Json(new { error = "Não autorizado" }, statusCode: 403);
            if (proposal.Status != ProposalStatus.Sent && proposal.Status != ProposalStatus.Negotiating)
                return Results.Json(new { error = $"Proposta em status '{proposal.Status}' não pode ser aceita" }, statusCode: 422);
            if (proposal.ValidUntil < DateTime.UtcNow)
                return Results.Json(new { error = "Proposta expirada" }, statusCode: 422);

            var orderId = Guid.NewGuid().ToString();
            var signalCents = (int)(proposal.PriceTotalCents * 0.3); // default 30%
            var balanceCents = proposal.PriceTotalCents - signalCents;

            var order = Order.CreateFromProposal(
                id: orderId,
                clientId: body.ClientId,
                professionalId: proposal.ProfessionalId,
                serviceId: proposal.ServiceId,
                tierId: 2, // Tier 2/3 proposals
                proposalId: proposal.Id,
                priceTotalCents: proposal.PriceTotalCents,
                signalCents: signalCents,
                balanceCents: balanceCents,
                installments: body.Installments ?? 1,
                paymentMethod: body.PaymentMethod,
                scope: proposal.Scope,
                scheduledAt: proposal.SuggestedDatetime,
                conversationId: proposal.ConversationId,
                addressId: body.AddressId);

            var created = await orderRepo.CreateFromProposalAsync(order, ct);
            await proposalRepo.AcceptAsync(proposalId, orderId, ct);

            await timeline.AddEventAsync(OrderTimeline.Create(
                id: Guid.NewGuid().ToString(),
                orderId: created.Id,
                eventType: "order_created_from_proposal",
                actorId: body.ClientId,
                actorRole: ActorRole.Client,
                metadata: $"{{\"proposalId\":\"{proposalId}\"}}"), ct);

            return Results.Json(created, statusCode: 201);
        });

        // ─── PUT /orders/{id}/status — actor-based transition ─────────────────
        app.MapPut("/orders/{id}/status", async (
            string id,
            UpdateOrderStatusRequest body,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            CancellationToken ct) =>
        {
            var order = await orderRepo.GetByIdAsync(id, ct);
            if (order is null)
                return Results.NotFound(new { error = "Pedido não encontrado" });

            if (!OrderRules.CanTransition(order.Status, body.NewStatus, body.ActorRole))
                return Results.Json(
                    new { error = $"Transição '{order.Status}' → '{body.NewStatus}' não permitida para ator '{body.ActorRole}'" },
                    statusCode: 422);

            bool updated;
            var newStatus = body.NewStatus.ToLowerInvariant();

            if (newStatus == OrderStatus.AwaitingConfirmation)
                updated = await orderRepo.MarkAwaitingConfirmationAsync(id, 72, ct);
            else if (newStatus == OrderStatus.Completed)
                updated = await orderRepo.MarkCompletedAsync(id, ct);
            else if (newStatus == OrderStatus.CancelledClient)
                updated = await orderRepo.MarkCancelledByClientAsync(id, body.Reason, ct);
            else if (newStatus == OrderStatus.CancelledProfessional)
                updated = await orderRepo.MarkCancelledByProfessionalAsync(id, body.Reason, ct);
            else if (newStatus == OrderStatus.Disputed)
                updated = await orderRepo.MarkDisputedAsync(id, ct);
            else
                updated = await orderRepo.UpdateStatusAsync(id, newStatus, ct);

            if (!updated)
                return Results.Json(new { error = "Falha ao atualizar status" }, statusCode: 500);

            await timeline.AddEventAsync(OrderTimeline.Create(
                id: Guid.NewGuid().ToString(),
                orderId: id,
                eventType: $"status_changed_to_{newStatus}",
                actorId: body.ActorId,
                actorRole: body.ActorRole,
                metadata: body.Reason is not null ? $"{{\"reason\":\"{body.Reason}\"}}" : null), ct);

            return Results.Ok(new { ok = true, status = newStatus });
        });

        // ─── POST /orders/{id}/confirm-completion — cliente confirma conclusão ─
        app.MapPost("/orders/{id}/confirm-completion", async (
            string id,
            HttpRequest req,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            CancellationToken ct) =>
        {
            var clientId = req.Query["clientId"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Json(new { error = "clientId é obrigatório" }, statusCode: 400);

            var order = await orderRepo.GetByIdAsync(id, ct);
            if (order is null)
                return Results.NotFound(new { error = "Pedido não encontrado" });
            if (order.ClientId != clientId)
                return Results.Json(new { error = "Não autorizado" }, statusCode: 403);
            if (order.Status != OrderStatus.AwaitingConfirmation)
                return Results.Json(new { error = "Pedido não está aguardando confirmação" }, statusCode: 422);

            await orderRepo.MarkCompletedAsync(id, ct);

            await timeline.AddEventAsync(OrderTimeline.Create(
                id: Guid.NewGuid().ToString(),
                orderId: id,
                eventType: "completion_confirmed_by_client",
                actorId: clientId,
                actorRole: ActorRole.Client), ct);

            return Results.Ok(new { ok = true, status = OrderStatus.Completed });
        });

        // ─── POST /orders/{id}/dispute — abrir disputa ────────────────────────
        app.MapPost("/orders/{id}/dispute", async (
            string id,
            HttpRequest req,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            CancellationToken ct) =>
        {
            var clientId = req.Query["clientId"].FirstOrDefault();
            var reason = req.Query["reason"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Json(new { error = "clientId é obrigatório" }, statusCode: 400);

            var order = await orderRepo.GetByIdAsync(id, ct);
            if (order is null)
                return Results.NotFound(new { error = "Pedido não encontrado" });
            if (order.ClientId != clientId)
                return Results.Json(new { error = "Não autorizado" }, statusCode: 403);
            if (order.Status != OrderStatus.AwaitingConfirmation && order.Status != OrderStatus.InProgress)
                return Results.Json(new { error = "Disputa só pode ser aberta quando pedido está em andamento ou aguardando confirmação" }, statusCode: 422);

            await orderRepo.MarkDisputedAsync(id, ct);

            await timeline.AddEventAsync(OrderTimeline.Create(
                id: Guid.NewGuid().ToString(),
                orderId: id,
                eventType: "dispute_opened",
                actorId: clientId,
                actorRole: ActorRole.Client,
                metadata: reason is not null ? $"{{\"reason\":\"{reason}\"}}" : null), ct);

            return Results.Ok(new { ok = true, status = OrderStatus.Disputed });
        });

        // ─── GET /orders/{id}/timeline ────────────────────────────────────────
        app.MapGet("/orders/{id}/timeline", async (
            string id,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            CancellationToken ct) =>
        {
            var order = await orderRepo.GetByIdAsync(id, ct);
            if (order is null)
                return Results.NotFound(new { error = "Pedido não encontrado" });

            var events = await timeline.GetByOrderIdAsync(id, ct);
            return Results.Ok(events);
        });

        // ─── GET /orders/mine?role=client|professional ────────────────────────
        app.MapGet("/orders/mine-v2", async (
            string? userId, string? role,
            IOrderRepository orderRepo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Json(new { error = "userId é obrigatório" }, statusCode: 400);
            var normalizedRole = (role ?? "client").ToLowerInvariant();
            if (normalizedRole != "client" && normalizedRole != "professional")
                return Results.Json(new { error = "role deve ser 'client' ou 'professional'" }, statusCode: 400);

            return Results.Ok(await orderRepo.GetMineByRoleAsync(userId, normalizedRole, ct));
        });

        // ─── Internal job endpoints (EventBridge targets) ─────────────────────
        app.MapPost("/internal/jobs/auto-confirmation", async (
            HttpRequest req,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var secret = config["INTERNAL_JOB_SECRET"];
            if (!string.IsNullOrWhiteSpace(secret) &&
                req.Headers.TryGetValue(InternalSecretHeader, out var provided) &&
                provided.ToString() != secret)
                return Results.Json(new { error = "Unauthorized" }, statusCode: 401);

            var now = DateTime.UtcNow;
            var overdueOrders = await orderRepo.GetOrdersAwaitingAutoConfirmAsync(now, ct);
            var completed = 0;

            foreach (var order in overdueOrders)
            {
                await orderRepo.MarkCompletedAsync(order.Id, ct);
                await timeline.AddEventAsync(OrderTimeline.Create(
                    id: Guid.NewGuid().ToString(),
                    orderId: order.Id,
                    eventType: "auto_confirmed_by_system",
                    actorId: null,
                    actorRole: ActorRole.System,
                    metadata: $"{{\"triggeredAt\":\"{now:O}\"}}"), ct);
                completed++;
            }

            return Results.Ok(new { processed = completed });
        });

        app.MapPost("/internal/jobs/payment-timeout", async (
            HttpRequest req,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var secret = config["INTERNAL_JOB_SECRET"];
            if (!string.IsNullOrWhiteSpace(secret) &&
                req.Headers.TryGetValue(InternalSecretHeader, out var provided) &&
                provided.ToString() != secret)
                return Results.Json(new { error = "Unauthorized" }, statusCode: 401);

            // Cancel orders awaiting payment for more than 24h
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var timedOut = await orderRepo.GetOrdersAwaitingPaymentTimedOutAsync(cutoff, ct);
            var cancelled = 0;

            foreach (var order in timedOut)
            {
                await orderRepo.MarkCancelledByClientAsync(order.Id, "payment_timeout", ct);
                await timeline.AddEventAsync(OrderTimeline.Create(
                    id: Guid.NewGuid().ToString(),
                    orderId: order.Id,
                    eventType: "cancelled_payment_timeout",
                    actorId: null,
                    actorRole: ActorRole.System,
                    metadata: $"{{\"cutoff\":\"{cutoff:O}\"}}"), ct);
                cancelled++;
            }

            return Results.Ok(new { processed = cancelled });
        });

        return app;
    }
}
