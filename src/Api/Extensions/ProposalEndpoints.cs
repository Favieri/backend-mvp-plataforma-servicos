using Application.Abstractions;
using Application.DTOs;
using Application.Services;
using Domain.Entities;
using Domain.Enums;

namespace Api.Extensions;

public static class ProposalEndpoints
{
    public static IEndpointRouteBuilder MapProposalEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── POST /proposals — profissional cria proposta ────────────────────
        app.MapPost("/proposals", async (
            CreateProposalRequest body,
            IProposalRepository repo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.ClientId))
                return Results.Json(new { error = "clientId é obrigatório" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.ServiceId))
                return Results.Json(new { error = "serviceId é obrigatório" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(body.Scope))
                return Results.Json(new { error = "scope é obrigatório" }, statusCode: 400);
            if (body.PriceTotalCents <= 0)
                return Results.Json(new { error = "priceTotalCents deve ser positivo" }, statusCode: 400);
            if (!DateTime.TryParse(body.ValidUntil, out var validUntil) || validUntil <= DateTime.UtcNow)
                return Results.Json(new { error = "validUntil deve ser uma data futura válida" }, statusCode: 400);

            DateTime? suggestedDatetime = DateTime.TryParse(body.SuggestedDatetime, out var sd) ? sd : null;

            var proposal = Proposal.Create(
                id: Guid.NewGuid().ToString(),
                professionalId: body.ProfessionalId,
                clientId: body.ClientId,
                serviceId: body.ServiceId,
                scope: body.Scope,
                priceTotalCents: body.PriceTotalCents,
                validUntil: validUntil,
                professionalServiceId: body.ProfessionalServiceId,
                conversationId: body.ConversationId,
                includesDescription: body.IncludesDescription,
                excludesDescription: body.ExcludesDescription,
                priceByStage: body.PriceByStage,
                durationEstimate: body.DurationEstimate,
                suggestedDatetime: suggestedDatetime,
                visitFeeCents: body.VisitFeeCents);

            var created = await repo.CreateAsync(proposal, ct);
            return Results.Json(ToDto(created), statusCode: 201);
        });

        // ─── GET /proposals/{id} ─────────────────────────────────────────────
        app.MapGet("/proposals/{id}", async (
            string id,
            IProposalRepository repo,
            CancellationToken ct) =>
        {
            var proposal = await repo.GetByIdAsync(id, ct);
            return proposal is null
                ? Results.NotFound(new { error = "Proposta não encontrada" })
                : Results.Ok(ToDto(proposal));
        });

        // ─── PUT /proposals/{id}/send — profissional envia ao cliente ─────────
        app.MapPut("/proposals/{id}/send", async (
            string id,
            SendProposalRequest body,
            IProposalRepository repo,
            CancellationToken ct) =>
        {
            var proposal = await repo.GetByIdAsync(id, ct);
            if (proposal is null)
                return Results.NotFound(new { error = "Proposta não encontrada" });
            if (proposal.ProfessionalId != body.ProfessionalId)
                return Results.Json(new { error = "Não autorizado" }, statusCode: 403);
            if (proposal.Status != ProposalStatus.Draft && proposal.Status != ProposalStatus.Negotiating)
                return Results.Json(new { error = $"Proposta em status '{proposal.Status}' não pode ser enviada" }, statusCode: 422);

            var ok = await repo.SendAsync(id, ct);
            return ok ? Results.Ok(new { ok = true, status = ProposalStatus.Sent })
                      : Results.Json(new { error = "Falha ao enviar proposta" }, statusCode: 500);
        });

        // ─── POST /proposals/{id}/accept — cliente aceita ────────────────────
        app.MapPost("/proposals/{id}/accept", async (
            string id,
            AcceptProposalRequest body,
            IProposalRepository repo,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            IUserRepository userRepo,
            CancellationToken ct) =>
        {
            var proposal = await repo.GetByIdAsync(id, ct);
            if (proposal is null)
                return Results.NotFound(new { error = "Proposta não encontrada" });
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
            var signalCents = (int)(proposal.PriceTotalCents * 0.3);
            var balanceCents = proposal.PriceTotalCents - signalCents;

            var order = Order.CreateFromProposal(
                id: orderId,
                clientId: body.ClientId,
                professionalId: proposal.ProfessionalId,
                serviceId: proposal.ServiceId,
                tierId: 2,
                proposalId: proposal.Id,
                priceTotalCents: proposal.PriceTotalCents,
                signalCents: signalCents,
                balanceCents: balanceCents,
                installments: body.Installments ?? 1,
                paymentMethod: body.PaymentMethod,
                scope: proposal.Scope,
                scheduledAt: proposal.SuggestedDatetime,
                conversationId: proposal.ConversationId,
                serviceAddress: resolvedAddress);

            var created = await orderRepo.CreateFromProposalAsync(order, ct);
            await repo.AcceptAsync(id, orderId, ct);

            await timeline.AddEventAsync(OrderTimeline.Create(
                id: Guid.NewGuid().ToString(),
                orderId: created.Id,
                eventType: "order_created_from_proposal",
                actorId: body.ClientId,
                actorRole: ActorRole.Client,
                metadata: $"{{\"proposalId\":\"{id}\"}}"), ct);

            return Results.Json(new { ok = true, orderId = created.Id, order = created }, statusCode: 201);
        });

        // ─── POST /proposals/{id}/reject — cliente rejeita ───────────────────
        app.MapPost("/proposals/{id}/reject", async (
            string id,
            RejectProposalRequest body,
            IProposalRepository repo,
            CancellationToken ct) =>
        {
            var proposal = await repo.GetByIdAsync(id, ct);
            if (proposal is null)
                return Results.NotFound(new { error = "Proposta não encontrada" });
            if (proposal.ClientId != body.ClientId)
                return Results.Json(new { error = "Não autorizado" }, statusCode: 403);
            if (proposal.Status != ProposalStatus.Sent && proposal.Status != ProposalStatus.Negotiating)
                return Results.Json(new { error = $"Proposta em status '{proposal.Status}' não pode ser rejeitada" }, statusCode: 422);

            var ok = await repo.RejectAsync(id, body.Reason, ct);
            return ok ? Results.Ok(new { ok = true, status = ProposalStatus.Rejected })
                      : Results.Json(new { error = "Falha ao rejeitar proposta" }, statusCode: 500);
        });

        // ─── POST /proposals/{id}/negotiate — contraproposta ─────────────────
        app.MapPost("/proposals/{id}/negotiate", async (
            string id,
            NegotiateProposalRequest body,
            IProposalRepository repo,
            CancellationToken ct) =>
        {
            var proposal = await repo.GetByIdAsync(id, ct);
            if (proposal is null)
                return Results.NotFound(new { error = "Proposta não encontrada" });
            if (proposal.ClientId != body.ActorId && proposal.ProfessionalId != body.ActorId)
                return Results.Json(new { error = "Não autorizado" }, statusCode: 403);
            if (proposal.Status == ProposalStatus.Accepted || proposal.Status == ProposalStatus.Rejected || proposal.Status == ProposalStatus.Expired)
                return Results.Json(new { error = $"Proposta em status '{proposal.Status}' não pode entrar em negociação" }, statusCode: 422);

            var ok = await repo.StartNegotiationAsync(id, ct);
            return ok ? Results.Ok(new { ok = true, status = ProposalStatus.Negotiating })
                      : Results.Json(new { error = "Falha ao iniciar negociação" }, statusCode: 500);
        });

        // ─── GET /proposals?conversationId=X ─────────────────────────────────
        app.MapGet("/proposals", async (
            string? conversationId,
            string? userId,
            string? role,
            IProposalRepository repo,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(conversationId))
            {
                var proposals = await repo.GetByConversationAsync(conversationId, ct);
                return Results.Ok(proposals.Select(ToDto));
            }

            if (!string.IsNullOrWhiteSpace(userId))
            {
                var normalizedRole = (role ?? "client").ToLowerInvariant();
                return Results.Ok(await repo.GetMineAsync(userId, normalizedRole, ct));
            }

            return Results.Json(new { error = "conversationId ou userId é obrigatório" }, statusCode: 400);
        });

        return app;
    }

    private static ProposalDto ToDto(Proposal p) => new(
        p.Id, p.OrderId, p.ProfessionalId, p.ClientId, p.ServiceId,
        p.ProfessionalServiceId, p.ConversationId, p.Scope,
        p.IncludesDescription, p.ExcludesDescription, p.PriceTotalCents,
        p.PriceByStage, p.DurationEstimate, p.SuggestedDatetime,
        p.VisitFeeCents, p.ValidUntil, p.Status, p.RejectionReason,
        p.CreatedAt, p.UpdatedAt);
}
