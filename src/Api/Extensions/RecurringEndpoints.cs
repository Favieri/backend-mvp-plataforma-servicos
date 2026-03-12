using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Extensions;

public static class RecurringEndpoints
{
    private const string InternalSecretHeader = "X-Internal-Secret";

    public static IEndpointRouteBuilder MapRecurringEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── POST /orders/rebook/{orderId} ─────────────────────────────────────
        // Recontrata um serviço a partir de um pedido concluído.
        // Opcionalmente cria um plano recorrente com desconto.
        app.MapPost("/orders/rebook/{orderId}", async (
            string orderId,
            RebookOrderRequest body,
            IOrderRepository orderRepo,
            IOrderTimelineRepository timeline,
            IRecurringPlanRepository recurringRepo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ClientId))
                return Results.Json(new { error = "clientId é obrigatório" }, statusCode: 400);

            // Load the source order
            var source = await orderRepo.GetByIdAsync(orderId, ct);
            if (source is null)
                return Results.NotFound(new { error = "Pedido original não encontrado" });
            if (source.ClientId != body.ClientId)
                return Results.Json(new { error = "Não autorizado" }, statusCode: 403);
            var completedStatuses = new[] { OrderStatus.Completed, OrderStatus.Evaluated, OrderStatus.Concluido, OrderStatus.AutoConcluido };
            if (!completedStatuses.Contains(source.Status))
                return Results.Json(new { error = "Recontratação só é permitida a partir de pedidos concluídos" }, statusCode: 422);
            if (source.ProfessionalId is null || source.ServiceId is null)
                return Results.Json(new { error = "Pedido original sem profissional ou serviço associado" }, statusCode: 422);

            // Validate recurring discount range
            if (body.DiscountPercent < 0 || body.DiscountPercent > 100)
                return Results.Json(new { error = "discountPercent deve estar entre 0 e 100" }, statusCode: 400);

            // Validate frequency
            var validFrequencies = new[] { RecurringFrequency.Weekly, RecurringFrequency.Biweekly, RecurringFrequency.Monthly };
            if (body.CreateRecurringPlan && !validFrequencies.Contains(body.Frequency))
                return Results.Json(new { error = "frequency inválido. Use: weekly, biweekly, monthly" }, statusCode: 400);

            var basePrice      = source.PriceTotalCents ?? 0;
            if (basePrice <= 0)
                return Results.Json(new { error = "Pedido original sem valor definido" }, statusCode: 422);

            // Apply recurring discount if plan will be created
            var effectivePrice = body.CreateRecurringPlan
                ? basePrice - (basePrice * body.DiscountPercent / 100)
                : basePrice;

            var signalCents  = (int)(effectivePrice * 0.3);
            var balanceCents = effectivePrice - signalCents;
            var scheduledAt  = DateTime.TryParse(body.ScheduledAt, out var parsed) ? parsed : (DateTime?)null;

            // Generate IDs up-front so the order can carry recurringPlanId from birth
            var newOrderId  = Guid.NewGuid().ToString();
            var planId      = body.CreateRecurringPlan ? Guid.NewGuid().ToString() : null;

            // Create the rebook order (recurringPlanId set from the start when a plan is requested)
            var order = Order.CreateRebook(
                id:              newOrderId,
                clientId:        body.ClientId,
                professionalId:  source.ProfessionalId,
                serviceId:       source.ServiceId,
                tierId:          source.TierId ?? 1,
                priceTotalCents: effectivePrice,
                signalCents:     signalCents,
                balanceCents:    balanceCents,
                installments:    body.Installments ?? 1,
                paymentMethod:   body.PaymentMethod ?? source.PaymentMethod,
                scope:           source.Scope,
                scheduledAt:     scheduledAt,
                addressId:       body.AddressId ?? source.AddressId,
                recurringPlanId: planId,
                description:     $"Recontratação do pedido {orderId}");

            var created = await orderRepo.CreateBookingAsync(order, ct);

            // Optionally create recurring plan
            RecurringPlan? plan = null;
            if (body.CreateRecurringPlan)
            {
                plan = RecurringPlan.Create(
                    id:               planId!,
                    clientId:         body.ClientId,
                    professionalId:   source.ProfessionalId,
                    serviceId:        source.ServiceId,
                    sourceOrderId:    orderId,
                    frequency:        body.Frequency,
                    priceTotalCents:  basePrice,
                    discountPercent:  body.DiscountPercent,
                    paymentMethod:    body.PaymentMethod ?? source.PaymentMethod,
                    scope:            source.Scope,
                    addressId:        body.AddressId ?? source.AddressId);

                plan = await recurringRepo.CreateAsync(plan, ct);
            }

            // Timeline event
            var meta = body.CreateRecurringPlan
                ? $"{{\"sourceOrderId\":\"{orderId}\",\"recurringPlanId\":\"{plan!.Id}\",\"frequency\":\"{body.Frequency}\",\"discountPercent\":{body.DiscountPercent}}}"
                : $"{{\"sourceOrderId\":\"{orderId}\"}}";

            await timeline.AddEventAsync(OrderTimeline.Create(
                id:        Guid.NewGuid().ToString(),
                orderId:   created.Id,
                eventType: "order_rebooked",
                actorId:   body.ClientId,
                actorRole: ActorRole.Client,
                metadata:  meta), ct);

            return Results.Json(new
            {
                order          = created,
                recurringPlan  = plan
            }, statusCode: 201);
        });

        // ─── GET /recurring-plans?clientId= ────────────────────────────────────
        app.MapGet("/recurring-plans", async (
            string? clientId,
            string? professionalId,
            IRecurringPlanRepository recurringRepo,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(clientId))
                return Results.Ok(await recurringRepo.GetByClientIdAsync(clientId, ct));
            if (!string.IsNullOrWhiteSpace(professionalId))
                return Results.Ok(await recurringRepo.GetByProfessionalIdAsync(professionalId, ct));

            return Results.Json(new { error = "clientId ou professionalId é obrigatório" }, statusCode: 400);
        });

        // ─── GET /recurring-plans/{id} ─────────────────────────────────────────
        app.MapGet("/recurring-plans/{id}", async (
            string id,
            IRecurringPlanRepository recurringRepo,
            CancellationToken ct) =>
        {
            var plan = await recurringRepo.GetByIdAsync(id, ct);
            return plan is null
                ? Results.NotFound(new { error = "Plano não encontrado" })
                : Results.Ok(plan);
        });

        // ─── GET /recurring-plans/{id}/occurrences ─────────────────────────────
        app.MapGet("/recurring-plans/{id}/occurrences", async (
            string id,
            IRecurringPlanRepository recurringRepo,
            CancellationToken ct) =>
        {
            var occurrences = await recurringRepo.GetOccurrencesByPlanIdAsync(id, ct);
            return Results.Ok(occurrences);
        });

        // ─── PATCH /recurring-plans/{id}/pause ────────────────────────────────
        app.MapPatch("/recurring-plans/{id}/pause", async (
            string id,
            PauseRecurringPlanRequest body,
            IRecurringPlanRepository recurringRepo,
            CancellationToken ct) =>
        {
            var plan = await recurringRepo.GetByIdAsync(id, ct);
            if (plan is null)
                return Results.NotFound(new { error = "Plano não encontrado" });
            if (plan.ClientId != body.ClientId)
                return Results.Json(new { error = "Não autorizado" }, statusCode: 403);
            if (plan.Status != RecurringPlanStatus.Active)
                return Results.Json(new { error = "Apenas planos ativos podem ser pausados" }, statusCode: 422);

            await recurringRepo.PauseAsync(id, ct);
            return Results.Ok(new { ok = true, status = RecurringPlanStatus.Paused });
        });

        // ─── PATCH /recurring-plans/{id}/resume ───────────────────────────────
        app.MapPatch("/recurring-plans/{id}/resume", async (
            string id,
            ResumeRecurringPlanRequest body,
            IRecurringPlanRepository recurringRepo,
            CancellationToken ct) =>
        {
            var plan = await recurringRepo.GetByIdAsync(id, ct);
            if (plan is null)
                return Results.NotFound(new { error = "Plano não encontrado" });
            if (plan.ClientId != body.ClientId)
                return Results.Json(new { error = "Não autorizado" }, statusCode: 403);
            if (plan.Status != RecurringPlanStatus.Paused)
                return Results.Json(new { error = "Apenas planos pausados podem ser retomados" }, statusCode: 422);

            await recurringRepo.ResumeAsync(id, ct);
            return Results.Ok(new { ok = true, status = RecurringPlanStatus.Active });
        });

        // ─── DELETE /recurring-plans/{id} ─────────────────────────────────────
        app.MapDelete("/recurring-plans/{id}", async (
            string id,
            CancelRecurringPlanRequest body,
            IRecurringPlanRepository recurringRepo,
            CancellationToken ct) =>
        {
            var plan = await recurringRepo.GetByIdAsync(id, ct);
            if (plan is null)
                return Results.NotFound(new { error = "Plano não encontrado" });
            if (plan.ClientId != body.ClientId)
                return Results.Json(new { error = "Não autorizado" }, statusCode: 403);
            if (plan.Status == RecurringPlanStatus.Cancelled)
                return Results.Json(new { error = "Plano já foi cancelado" }, statusCode: 422);

            await recurringRepo.CancelAsync(id, ct);
            return Results.Ok(new { ok = true, status = RecurringPlanStatus.Cancelled });
        });

        // ─── Internal: POST /internal/jobs/recurring-billing ──────────────────
        // EventBridge target — triggers the RecurringBillingJob logic on-demand in Lambda.
        app.MapPost("/internal/jobs/recurring-billing", async (
            HttpRequest req,
            IServiceProvider sp,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var secret = config["INTERNAL_JOB_SECRET"];
            if (!string.IsNullOrWhiteSpace(secret) &&
                req.Headers.TryGetValue(InternalSecretHeader, out var provided) &&
                provided.ToString() != secret)
                return Results.Json(new { error = "Unauthorized" }, statusCode: 401);

            // Resolve and run the job logic inline
            var job = sp.GetRequiredService<Infrastructure.BackgroundJobs.RecurringBillingJob>();
            await job.RunAsync(ct);

            return Results.Ok(new { ok = true });
        });

        return app;
    }
}
