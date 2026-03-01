using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Caching.Memory;

namespace Api.Extensions;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapMarketplaceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", version = "v1" }));

        app.MapGet("/professionals", async (
            HttpRequest req,
            string? zoneId,
            string? serviceId,
            IProfessionalReadRepository repo,
            IMemoryCache cache,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("HomeEndpoints");
            var bypassCache = ShouldBypassCache(req);
            var cacheKey = $"professionals:{zoneId ?? "*"}:{serviceId ?? "*"}";

            var professionals = await GetOrCreateCachedAsync(
                cache,
                cacheKey,
                TimeSpan.FromSeconds(45),
                bypassCache,
                () => repo.GetProfessionalsAsync(zoneId, serviceId, ct),
                logger,
                ct);

            return Results.Ok(professionals);
        });

        app.MapGet("/zones", async (HttpRequest req, IProfessionalReadRepository repo, IMemoryCache cache, CancellationToken ct) =>
        {
            var zones = await GetOrCreateCachedAsync(
                cache,
                "zones:active",
                TimeSpan.FromMinutes(10),
                ShouldBypassCache(req),
                () => repo.GetZonesAsync(ct),
                logger: null,
                ct);

            return Results.Ok(zones);
        });

        app.MapGet("/services", async (HttpRequest req, IProfessionalReadRepository repo, IMemoryCache cache, CancellationToken ct) =>
        {
            var services = await GetOrCreateCachedAsync(
                cache,
                "services:all",
                TimeSpan.FromMinutes(10),
                ShouldBypassCache(req),
                () => repo.GetServicesAsync(ct),
                logger: null,
                ct);

            return Results.Ok(services);
        });

        app.MapGet("/bootstrap", async (HttpRequest req, IProfessionalReadRepository repo, IMemoryCache cache, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("HomeEndpoints");
            var bypassCache = ShouldBypassCache(req);
            var bootstrap = await GetOrCreateCachedAsync(
                cache,
                "home:bootstrap",
                TimeSpan.FromSeconds(45),
                bypassCache,
                async () =>
                {
                    var professionalsTask = repo.GetProfessionalsAsync(zoneId: null, serviceId: null, ct);
                    var zonesTask = repo.GetZonesAsync(ct);
                    var servicesTask = repo.GetServicesAsync(ct);
                    await Task.WhenAll(professionalsTask, zonesTask, servicesTask);
                    return new HomeBootstrapDto(professionalsTask.Result, zonesTask.Result, servicesTask.Result);
                },
                logger,
                ct);

            return Results.Ok(bootstrap);
        });

        app.MapGet("/home/bootstrap", (HttpContext context) =>
            Results.Redirect($"/bootstrap{context.Request.QueryString}"));

        app.MapPost("/api/auth", async (LoginRequest body, IAuthRepository db, CancellationToken ct) =>
        {
            var user = await db.LoginAsync(body.Email, body.Senha, ct);
            return user is null ? Results.Json(new { error = "Credenciais inválidas" }, statusCode: 401) : Results.Ok(user);
        });

        app.MapGet("/api/orders", async (HttpContext context, IMemoryCache cache, string? serviceId, string? excludeProfessionalId, string? professionalId, bool? filterZones, IOrderRepository repo, CancellationToken ct)
            => await GetOrSetCachedListAsync(
                context,
                cache,
                "api-orders",
                TimeSpan.FromSeconds(30),
                async token => await repo.GetOrdersAsync(serviceId, excludeProfessionalId, professionalId, filterZones == true, token),
                ct));

        app.MapGet("/professionals", async (HttpContext context, IMemoryCache cache, string? serviceId, string? zoneId, string? excludeProfessionalId, string? professionalId, bool? filterZones, IProfessionalRepository repo, CancellationToken ct)
            => await GetOrSetCachedListAsync(
                context,
                cache,
                "professionals-cards",
                TimeSpan.FromSeconds(60),
                async token => await repo.GetProfessionalCardsAsync(serviceId, zoneId, excludeProfessionalId, professionalId, filterZones == true, token),
                ct));

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

    private static bool ShouldBypassCache(HttpRequest req)
        => req.Headers.TryGetValue("Cache-Control", out var values)
           && values.Any(v => v.Contains("no-cache", StringComparison.OrdinalIgnoreCase));

    private static async Task<T> GetOrCreateCachedAsync<T>(
        IMemoryCache cache,
        string key,
        TimeSpan ttl,
        bool bypass,
        Func<Task<T>> factory,
        ILogger? logger,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!bypass && cache.TryGetValue(key, out T? value) && value is not null)
        {
            logger?.LogDebug("Cache HIT {CacheKey}", key);
            return value;
        }

        logger?.LogDebug("Cache MISS {CacheKey} (bypass={Bypass})", key, bypass);
        var created = await factory();
        cache.Set(key, created, ttl);
        return created;
    }
}
