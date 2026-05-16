using Application.Abstractions;
using Application.DTOs;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Api.Extensions;

public static class OrderEndpoints
{
    private const string InternalSecretHeader = "X-Internal-Secret";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── GET /orders/{id} — detalhe enriquecido do pedido ─────────────────
        // Retorna order + professional (com nome via User) + service + client + timeline.
        // Endpoint estava ausente: o front-end chamava GET /orders/{id} e recebia 404.
        app.MapGet("/orders/{id}", async (
            string id,
            HttpContext context,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            AppDbContext ctx,
            CancellationToken ct) =>
        {
            var order = await orderRepo.GetByIdAsync(id, ct);
            if (order is null)
                return Results.NotFound(new { error = "Pedido não encontrado" });

            var jwtUserId = context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                         ?? context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(jwtUserId))
                return Results.Json(new { error = "Não autenticado" }, statusCode: 401);

            var events = await timeline.GetByOrderIdAsync(id, ct);

            var reviewRow = await ctx.Reviews
                .AsNoTracking()
                .Where(r => r.OrderId == order.Id)
                .Select(r => new { r.Id, r.Rating })
                .FirstOrDefaultAsync(ct);

            var requestingClientId = context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                                  ?? context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                  ?? context.Request.Query["clientId"].FirstOrDefault();

            var windowOk = (order.CompletedAt ?? order.CreatedAt) >= DateTime.UtcNow.AddDays(-30);
            var canReview = order.Status == OrderStatus.Completed
                && order.ClientId == requestingClientId
                && reviewRow is null
                && windowOk;

            var professional = order.ProfessionalId is not null
                ? await ctx.Professionals.AsNoTracking()
                    .Where(p => p.Id == order.ProfessionalId)
                    .Select(p => new
                    {
                        id = p.Id,
                        userId = p.UserId,
                        avatarUrl = p.AvatarUrl,
                        name = ctx.Users.Where(u => u.Id == p.UserId).Select(u => u.Name).FirstOrDefault()
                    })
                    .FirstOrDefaultAsync(ct)
                : null;

            var jwtRole = context.User?.FindFirst("role")?.Value ?? "";
            var isAdmin = string.Equals(jwtRole, "admin", StringComparison.OrdinalIgnoreCase);
            var isOrderClient = order.ClientId == jwtUserId;
            var isOrderProfessional = professional?.userId == jwtUserId || professional?.id == jwtUserId;
            if (!isOrderClient && !isOrderProfessional && !isAdmin)
                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

            var service = await ctx.Services.AsNoTracking()
                .Where(s => s.Id == order.ServiceId)
                .Select(s => new { id = s.Id, name = s.Name })
                .FirstOrDefaultAsync(ct);

            var client = await ctx.Users.AsNoTracking()
                .Where(u => u.Id == order.ClientId)
                .Select(u => new { id = u.Id, name = u.Name })
                .FirstOrDefaultAsync(ct);

            var svcAddr = order.GetServiceAddress();

            return Results.Ok(new
            {
                id = order.Id,
                status = order.Status,
                createdAt = order.CreatedAt,
                scheduledAt = order.ScheduledAt ?? order.Date,
                totalCents = order.PriceTotalCents,
                depositCents = order.SignalCents,
                notes = order.Description,
                location = order.Location,
                serviceAddress = svcAddr is not null ? new
                {
                    zipCode = svcAddr.ZipCode,
                    street = svcAddr.Street,
                    number = svcAddr.Number,
                    neighborhood = svcAddr.Neighborhood,
                    city = svcAddr.City,
                    state = svcAddr.State,
                    complement = svcAddr.Complement,
                    reference = svcAddr.Reference
                } : null,
                professional,
                client,
                service,
                timeline = events,
                canReview,
                reviewId = reviewRow?.Id,
                reviewRating = reviewRow?.Rating,
            });
        });

        // ─── POST /orders/booking — Tier 1 direct booking ─────────────────────
        app.MapPost("/orders/booking", async (
            CreateBookingRequest body,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            IServiceCatalogRepository catalog,
            IUserRepository userRepo,
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

            // Signal and installments are platform-fixed: 30% signal, 1 installment
            var signalCents  = (int)(body.PriceTotalCents * 0.3);
            var balanceCents = body.PriceTotalCents - signalCents;

            // Validate tier allows direct booking
            var tiers = await catalog.GetTiersAsync(ct);
            var tier = tiers.FirstOrDefault(t => t.Id == body.TierId);
            if (tier is null)
                return Results.Json(new { error = "tierId inválido" }, statusCode: 400);
            if (!tier.AllowBookingDirect)
                return Results.Json(new { error = "Este tier não permite booking direto. Use proposta." }, statusCode: 422);

            // Resolve service address
            AddressDto? defaultAddress = null;
            if (body.UseDefaultAddress)
                defaultAddress = await userRepo.GetDefaultAddressAsync(body.ClientId, ct);

            var (resolvedAddress, addrError) = AddressResolver.Resolve(
                body.UseDefaultAddress, body.ServiceAddress, defaultAddress);
            if (addrError is not null)
                return Results.Json(new { error = addrError }, statusCode: 422);

            var scheduledAt = DateTime.TryParse(body.ScheduledAt, out var parsed) ? parsed : (DateTime?)null;
            var order = Order.CreateBooking(
                id: Guid.NewGuid().ToString(),
                clientId: body.ClientId,
                professionalId: body.ProfessionalId,
                serviceId: body.ServiceId,
                tierId: body.TierId,
                priceTotalCents: body.PriceTotalCents,
                signalCents: signalCents,
                balanceCents: balanceCents,
                installments: 1,
                paymentMethod: body.PaymentMethod,
                scope: body.Scope,
                scheduledAt: scheduledAt,
                conversationId: body.ConversationId,
                description: body.Description,
                serviceAddress: resolvedAddress);

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
            IUserRepository userRepo,
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

            // Resolve service address
            AddressDto? defaultAddress = null;
            if (body.UseDefaultAddress)
                defaultAddress = await userRepo.GetDefaultAddressAsync(body.ClientId, ct);

            var (resolvedAddress, addrError) = AddressResolver.Resolve(
                body.UseDefaultAddress, body.ServiceAddress, defaultAddress);
            if (addrError is not null)
                return Results.Json(new { error = addrError }, statusCode: 422);

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
                installments: 1,
                paymentMethod: body.PaymentMethod,
                scope: proposal.Scope,
                scheduledAt: proposal.SuggestedDatetime,
                conversationId: proposal.ConversationId,
                serviceAddress: resolvedAddress);

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
            IPaymentRepository paymentRepo,
            IRefundService refundService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("OrderEndpoints");

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

            // Disparar reembolso automático em cancelamentos com pagamento confirmado.
            // Falha no reembolso NÃO bloqueia o cancelamento — fica pendente para reprocessamento.
            if (newStatus is OrderStatus.CancelledClient or OrderStatus.CancelledProfessional)
            {
                var paidPayment = await paymentRepo.GetPaidByOrderIdAsync(id, ct);
                if (paidPayment is not null)
                {
                    try
                    {
                        var refundReason = newStatus == OrderStatus.CancelledClient
                            ? "cancelled_by_client"
                            : "cancelled_by_professional";
                        await refundService.RefundOrderAsync(id, refundReason, amountCents: null, ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "[OrderEndpoints] Falha ao reembolsar automaticamente o pedido {OrderId}. Marcando como pendente.",
                            id);
                        await paymentRepo.MarkRefundPendingAsync(paidPayment.Id,
                            newStatus == OrderStatus.CancelledClient ? "cancelled_by_client" : "cancelled_by_professional",
                            ct);
                    }
                }
            }

            return Results.Ok(new { ok = true, status = newStatus });
        });

        // ─── POST /orders/{id}/confirm-completion — cliente confirma conclusão ─
        // clientId aceito por prioridade: (1) claim Sub do JWT, (2) query param ?clientId=
        app.MapPost("/orders/{id}/confirm-completion", async (
            string id,
            HttpContext context,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            IPaymentRepository paymentRepo,
            ILedgerRepository ledgerRepo,
            CancellationToken ct) =>
        {
            // Prioridade: JWT claim > query param (retrocompatibilidade)
            var clientId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                        ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? context.Request.Query["clientId"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Json(new { error = "clientId é obrigatório (envie via JWT ou ?clientId=)" }, statusCode: 400);

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

            // Release earning to professional ledger
            if (order.ProfessionalId is not null)
            {
                var payment = await paymentRepo.GetByOrderIdAsync(id, ct);
                if (payment is not null)
                {
                    var earningCents = payment.AmountCents - payment.PlatformFeeCents - payment.GatewayFeeCents;
                    if (earningCents > 0)
                    {
                        await ledgerRepo.AddAsync(LedgerEntry.Create(
                            type: "earning_released",
                            orderId: id,
                            paymentId: payment.Id,
                            professionalId: order.ProfessionalId,
                            amountCents: earningCents), ct);
                    }
                }
            }

            return Results.Ok(new { ok = true, status = OrderStatus.Completed });
        });

        // ─── POST /orders/{id}/dispute — abrir disputa ────────────────────────
        // clientId aceito via JWT, query param ou body JSON.
        // reason aceito via body JSON (campo "reason") ou query param.
        app.MapPost("/orders/{id}/dispute", async (
            string id,
            HttpContext context,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            CancellationToken ct) =>
        {
            // Ler body JSON para clientId e reason (evita binding de tipo privado)
            string? bodyClientId = null;
            string? bodyReason = null;
            if (context.Request.HasJsonContentType())
            {
                try
                {
                    using var bodyDoc = await System.Text.Json.JsonDocument.ParseAsync(
                        context.Request.Body, cancellationToken: ct);
                    var bodyRoot = bodyDoc.RootElement;
                    if (bodyRoot.TryGetProperty("clientId", out var cEl))
                        bodyClientId = cEl.GetString();
                    if (bodyRoot.TryGetProperty("reason", out var rEl))
                        bodyReason = rEl.GetString();
                }
                catch { /* fallback silencioso */ }
            }

            var clientId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                        ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? bodyClientId
                        ?? context.Request.Query["clientId"].FirstOrDefault();

            var reason = bodyReason
                      ?? context.Request.Query["reason"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Json(new { error = "clientId é obrigatório (envie via JWT, body ou ?clientId=)" }, statusCode: 400);

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
                metadata: reason is not null ? $"{{\"reason\":\"{reason.Replace("\"", "\\\"")}\"}}": null), ct);

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
            HttpRequest req,
            IOrderRepository orderRepo,
            CancellationToken ct) =>
        {
            var jwtUserId = req.HttpContext.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                         ?? req.HttpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(jwtUserId))
                return Results.Json(new { error = "Não autenticado" }, statusCode: 401);

            var role = req.HttpContext.User?.FindFirst("role")?.Value ?? "cliente";
            var normalizedRole = role == "profissional" ? "professional" : "client";
            return Results.Ok(await orderRepo.GetMineByRoleAsync(jwtUserId, normalizedRole, ct));
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
