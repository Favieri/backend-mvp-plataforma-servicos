using System.Security.Claims;
using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using FluentValidation;

namespace Api.Extensions;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapMarketplaceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", version = "v1" }));

        app.MapGet("/cors-test", (HttpContext context) =>
        {
            var origin = context.Request.Headers.Origin.FirstOrDefault();
            return Results.Ok(new { origin, ok = true });
        });

        app.MapPost("/api/auth", async (LoginRequest body, IAuthRepository db, CancellationToken ct) =>
        {
            var user = await db.LoginAsync(body.Email, body.Senha, ct);
            return user is null ? Results.Json(new { error = "Credenciais inválidas" }, statusCode: 401) : Results.Ok(user);
        });

        app.MapGet("/api/orders", async (string? serviceId, string? excludeProfessionalId, string? professionalId, bool? filterZones, IOrderRepository repo, CancellationToken ct)
            => Results.Ok(await repo.GetOrdersAsync(serviceId, excludeProfessionalId, professionalId, filterZones == true, ct)));

        app.MapGet("/professionals", async (string? serviceId, string? excludeProfessionalId, string? professionalId, bool? filterZones, IOrderRepository repo, CancellationToken ct)
            => Results.Ok(await repo.GetOrdersAsync(serviceId, excludeProfessionalId, professionalId, filterZones == true, ct)));

        app.MapPost("/api/orders", async (CreateOrderRequest body, IValidator<CreateOrderRequest> validator, IOrderRepository repo, CancellationToken ct) =>
        {
            var val = await validator.ValidateAsync(body, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            var date = DateTime.TryParse(body.Date, out var parsed) ? parsed : (DateTime?)null;
            var created = await repo.CreateAsync(body.ClientId, body.ServiceId, body.Description, body.Location, date, ct);
            return Results.Json(created, statusCode: 201);
        });

        app.MapGet("/api/orders/mine", async (string clientId, IOrderRepository repo, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(clientId) ? Results.Json(new { error = "clientId é obrigatório" }, statusCode: 400) : Results.Ok(await repo.GetMineAsync(clientId, ct)));

        app.MapPost("/api/orders/{id}/complete", async (string id, CompleteOrderRequest _, IOrderRepository repo, CancellationToken ct) =>
        {
            var order = await repo.GetByIdAsync(id, ct);
            if (order is null) return Results.NotFound(new { error = "Pedido não encontrado" });
            await repo.CompleteOrderAsync(id, ct);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/appointments/mine", async (string clientId, IAppointmentRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetByClientAsync(clientId, ct)));

        app.MapPost("/api/appointments", async (CreateAppointmentRequest body, IAppointmentRepository repo, CancellationToken ct) =>
        {
            var created = await repo.CreateAsync(new Appointment(Guid.NewGuid().ToString(), body.ProfessionalId, body.ClientId, body.ServiceId, body.StartsAt, body.EndsAt, "PENDING", body.Location, body.Notes), ct);
            return Results.Json(created, statusCode: 201);
        });

        app.MapPut("/api/appointments/{id}", async (string id, UpdateAppointmentStatusRequest body, IAppointmentRepository repo, CancellationToken ct) =>
        {
            var allowed = new[] { "CONFIRMED", "CANCELLED" };
            if (!allowed.Contains(body.Status, StringComparer.OrdinalIgnoreCase)) return Results.Json(new { error = "Status inválido." }, statusCode: 400);
            var updated = await repo.UpdateStatusAsync(id, body.Status.ToUpperInvariant(), ct);
            return updated is null ? Results.NotFound(new { error = "Agendamento não encontrado." }) : Results.Ok(updated);
        });

        app.MapPost("/api/payments/preference", async (CreatePaymentPreferenceRequest body, IValidator<CreatePaymentPreferenceRequest> validator, IMercadoPagoClient mp, IPaymentRepository paymentRepo, CancellationToken ct) =>
        {
            var val = await validator.ValidateAsync(body, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            var (preferenceId, initPoint) = await mp.CreatePreferenceAsync(body.OrderId, body.AmountCents, body.Title ?? "Serviço", ct);
            var payment = await paymentRepo.UpsertAsync(new Payment(Guid.NewGuid().ToString(), body.OrderId, "mercado_pago", preferenceId, body.Method ?? "pix", body.AmountCents, "pending", DateTime.UtcNow, null), ct);
            return Results.Ok(new { paymentId = payment.Id, preferenceId, initPoint, status = payment.Status });
        });

        app.MapGet("/api/payments/{orderId}", async (string orderId, IPaymentRepository repo, CancellationToken ct) =>
        {
            var payment = await repo.GetLatestByOrderAsync(orderId, ct);
            return payment is null ? Results.NotFound(new { error = "Pagamento não encontrado" }) : Results.Ok(payment);
        });

        app.MapPost("/webhooks/mercadopago", async (HttpRequest req, MercadoPagoWebhookRequest body, IPaymentRepository repo, IMercadoPagoClient mp, CancellationToken ct) =>
        {
            var eventId = body.Id ?? req.Headers["x-idempotency-key"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
            var raw = await new StreamReader(req.Body).ReadToEndAsync(ct);
            if (!await repo.TryStartWebhookProcessingAsync("mercado_pago", eventId, raw, ct)) return Results.Ok(new { duplicated = true });
            var paymentId = body.Data is not null && body.Data.TryGetValue("id", out var idObj) ? idObj?.ToString() : null;
            if (!string.IsNullOrWhiteSpace(paymentId))
            {
                var status = await mp.GetPaymentStatusAsync(paymentId!, ct);
                await repo.ApplyPaymentStatusAsync(paymentId!, status, status == "approved" ? DateTime.UtcNow : null, ct);
            }
            await repo.MarkWebhookProcessedAsync("mercado_pago", eventId, ct);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/wallet/balance", async (string professionalId, IPaymentRepository repo, CancellationToken ct)
            => Results.Ok(new { professionalId, balanceCents = await repo.GetWalletBalanceAsync(professionalId, ct) }));

        app.MapGet("/api/wallet/ledger", async (string professionalId, IPaymentRepository repo, CancellationToken ct)
            => Results.Ok(await repo.GetLedgerAsync(professionalId, ct)));

        return app;
    }
}
