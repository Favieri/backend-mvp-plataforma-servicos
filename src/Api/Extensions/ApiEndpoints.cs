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
        // ─── Health ────────────────────────────────────────────────────────────
        app.MapGet("/health", () => Results.Ok(new { status = "ok", version = "v1" }));

        // ─── Public marketplace (cached) ───────────────────────────────────────
        app.MapGet("/professionals", async (
            HttpRequest req, string? zoneId, string? serviceId,
            IProfessionalReadRepository repo, IMemoryCache cache, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("HomeEndpoints");
            var cacheKey = $"professionals:{zoneId ?? "*"}:{serviceId ?? "*"}";
            var professionals = await GetOrCreateCachedAsync(cache, cacheKey, TimeSpan.FromSeconds(45), ShouldBypassCache(req),
                () => repo.GetProfessionalsAsync(zoneId, serviceId, ct), logger, ct);
            return Results.Ok(professionals);
        });

        app.MapGet("/zones", async (HttpRequest req, IProfessionalReadRepository repo, IMemoryCache cache, CancellationToken ct) =>
        {
            var zones = await GetOrCreateCachedAsync(cache, "zones:active", TimeSpan.FromMinutes(10), ShouldBypassCache(req),
                () => repo.GetZonesAsync(ct), logger: null, ct);
            return Results.Ok(zones);
        });

        app.MapGet("/services", async (HttpRequest req, IProfessionalReadRepository repo, IMemoryCache cache, CancellationToken ct) =>
        {
            var services = await GetOrCreateCachedAsync(cache, "services:all", TimeSpan.FromMinutes(10), ShouldBypassCache(req),
                () => repo.GetServicesAsync(ct), logger: null, ct);
            return Results.Ok(services);
        });

        app.MapGet("/bootstrap", async (HttpRequest req, IProfessionalReadRepository repo, IMemoryCache cache, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("HomeEndpoints");
            var bootstrap = await GetOrCreateCachedAsync(cache, "home:bootstrap", TimeSpan.FromSeconds(45), ShouldBypassCache(req),
                async () =>
                {
                    var t1 = repo.GetProfessionalsAsync(null, null, ct);
                    var t2 = repo.GetZonesAsync(ct);
                    var t3 = repo.GetServicesAsync(ct);
                    await Task.WhenAll(t1, t2, t3);
                    return new HomeBootstrapDto(t1.Result, t2.Result, t3.Result);
                }, logger, ct);
            return Results.Ok(bootstrap);
        });

        app.MapGet("/home/bootstrap", (HttpContext ctx) =>
            Results.Redirect($"/bootstrap{ctx.Request.QueryString}"));

        // ─── Auth ──────────────────────────────────────────────────────────────
        app.MapPost("/api/auth", async (LoginRequest body, IAuthRepository db, CancellationToken ct) =>
        {
            var user = await db.LoginAsync(body.Email, body.Senha, ct);
            return user is null ? Results.Json(new { error = "Credenciais inválidas" }, statusCode: 401) : Results.Ok(user);
        });

        // ─── Users ─────────────────────────────────────────────────────────────
        app.MapPost("/api/users", async (CreateUserRequest body, IUserRepository repo, CancellationToken ct) =>
        {
            var name = body.Name?.Trim() ?? "";
            var email = body.Email?.Trim() ?? "";
            var role = body.Role?.Trim() ?? "";
            var senha = body.Senha ?? "";

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(role) || string.IsNullOrEmpty(senha))
                return Results.Json(new { error = "name, email, role e senha são obrigatórios" }, statusCode: 400);
            if (role != "cliente" && role != "profissional" && role != "admin")
                return Results.Json(new { error = "role inválido" }, statusCode: 400);
            if (role == "cliente" && string.IsNullOrWhiteSpace(body.ZoneId))
                return Results.Json(new { error = "zoneId é obrigatório para clientes" }, statusCode: 400);
            if (await repo.EmailExistsAsync(email, ct))
                return Results.Json(new { error = "Já existe um usuário com este email" }, statusCode: 400);
            if (!string.IsNullOrWhiteSpace(body.ZoneId) && !await repo.ZoneExistsAndActiveAsync(body.ZoneId, ct))
                return Results.Json(new { error = "zoneId inválido (zona inexistente ou inativa)" }, statusCode: 400);

            var hashed = BCrypt.Net.BCrypt.HashPassword(senha, workFactor: 10);
            var user = await repo.CreateAsync(name, email, body.Phone?.Trim(), role, hashed, body.ZoneId?.Trim(), ct);
            return Results.Json(user, statusCode: 201);
        });

        // ─── Professionals ─────────────────────────────────────────────────────
        app.MapGet("/api/professionals", async (
            HttpContext ctx, IMemoryCache cache, string? serviceId, string? zoneId,
            string? excludeProfessionalId, string? professionalId, bool? filterZones,
            IProfessionalRepository repo, CancellationToken ct) =>
            await GetOrSetCachedListAsync(ctx, cache, "professionals-cards", TimeSpan.FromSeconds(60),
                async token => await repo.GetProfessionalCardsAsync(serviceId, zoneId, excludeProfessionalId, professionalId, filterZones == true, token), ct));

        app.MapGet("/api/professionals/zones", async (string? professionalId, IProfessionalDetailRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            return Results.Ok(await repo.GetZonesAsync(professionalId, ct));
        });

        app.MapPut("/api/professionals/zones", async (UpdateProfessionalZonesRequest body, IProfessionalDetailRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            try
            {
                var result = await repo.UpdateZonesAsync(body.ProfessionalId, body.Zones ?? [], ct);
                return result is null ? Results.NotFound(new { error = "Profissional não encontrado" }) : Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
        });

        app.MapGet("/api/professionals/{id}", async (string id, IProfessionalDetailRepository repo, CancellationToken ct) =>
        {
            var pro = await repo.GetByIdAsync(id, ct);
            return pro is null ? Results.NotFound(new { error = "Profissional não encontrado." }) : Results.Ok(pro);
        });

        app.MapPut("/api/professionals/{id}", async (string id, UpdateProfessionalRequest body, IProfessionalDetailRepository repo, CancellationToken ct) =>
        {
            var updated = await repo.UpdateAsync(id, body.Bio, body.Active, body.AvailabilityText, body.AvatarUrl, ct);
            return updated is null ? Results.NotFound(new { error = "Profissional não encontrado." }) : Results.Ok(updated);
        });

        // ─── Professional Services ──────────────────────────────────────────────
        app.MapGet("/api/professional-services", async (string? professionalId, string? serviceId, IProfessionalServiceRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetAsync(professionalId, serviceId, ct)));

        app.MapPost("/api/professional-services", async (CreateProfessionalServiceRequest body, IProfessionalServiceRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId) || string.IsNullOrWhiteSpace(body.ServiceId) || string.IsNullOrWhiteSpace(body.NomeServico))
                return Results.Json(new { error = "professionalId, serviceId e nomeServico são obrigatórios" }, statusCode: 400);
            if (!await repo.ProfessionalExistsAsync(body.ProfessionalId, ct))
                return Results.Json(new { error = "Profissional não encontrado." }, statusCode: 400);
            if (!await repo.ServiceExistsAsync(body.ServiceId, ct))
                return Results.Json(new { error = "Serviço (categoria) não encontrado." }, statusCode: 400);
            var created = await repo.CreateAsync(body.ProfessionalId, body.ServiceId, body.NomeServico.Trim(), body.Preco, body.Descricao, ct);
            return Results.Json(created, statusCode: 201);
        });

        app.MapGet("/api/professional-services/{id}", async (string id, IProfessionalServiceRepository repo, CancellationToken ct) =>
        {
            var svc = await repo.GetByIdAsync(id, ct);
            return svc is null ? Results.NotFound(new { error = "Serviço não encontrado" }) : Results.Ok(svc);
        });

        app.MapPut("/api/professional-services/{id}", async (string id, UpdateProfessionalServiceRequest body, IProfessionalServiceRepository repo, CancellationToken ct) =>
        {
            var updated = await repo.UpdateAsync(id, body.NomeServico, body.Preco, body.Descricao, ct);
            return updated is null ? Results.NotFound(new { error = "Serviço não encontrado" }) : Results.Ok(updated);
        });

        app.MapDelete("/api/professional-services/{id}", async (string id, IProfessionalServiceRepository repo, CancellationToken ct) =>
        {
            var deleted = await repo.DeleteAsync(id, ct);
            return deleted ? Results.Ok(new { ok = true }) : Results.NotFound(new { error = "Serviço não encontrado" });
        });

        // ─── Orders ────────────────────────────────────────────────────────────
        app.MapGet("/api/orders", async (
            HttpContext ctx, IMemoryCache cache, string? serviceId, string? excludeProfessionalId,
            string? professionalId, bool? filterZones, IOrderRepository repo, CancellationToken ct) =>
            await GetOrSetCachedListAsync(ctx, cache, "api-orders", TimeSpan.FromSeconds(30),
                async token => await repo.GetOrdersAsync(serviceId, excludeProfessionalId, professionalId, filterZones == true, token), ct));

        app.MapPost("/api/orders", async (CreateOrderRequest body, IValidator<CreateOrderRequest> validator, IOrderRepository repo, CancellationToken ct) =>
        {
            var val = await validator.ValidateAsync(body, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            var date = DateTime.TryParse(body.Date, out var parsed) ? parsed : (DateTime?)null;
            var created = await repo.CreateAsync(body.ClientId, body.ServiceId, body.Description, body.Location, date, ct);
            return Results.Json(created, statusCode: 201);
        });

        app.MapGet("/api/orders/mine", async (string clientId, IOrderRepository repo, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(clientId)
                ? Results.Json(new { error = "clientId é obrigatório" }, statusCode: 400)
                : Results.Ok(await repo.GetMineAsync(clientId, ct)));

        app.MapPost("/api/orders/{id}/complete", async (string id, CompleteOrderRequest _, IOrderRepository repo, CancellationToken ct) =>
        {
            var order = await repo.GetByIdAsync(id, ct);
            if (order is null) return Results.NotFound(new { error = "Pedido não encontrado" });
            await repo.CompleteOrderAsync(id, ct);
            return Results.Ok(new { ok = true });
        });

        // ─── Appointments ──────────────────────────────────────────────────────
        app.MapGet("/api/appointments", async (
            string? professionalId, string? status, string? from, string? to,
            IAppointmentRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório." }, statusCode: 400);
            var fromDt = DateTime.TryParse(from, out var fp) ? fp : (DateTime?)null;
            var toDt = DateTime.TryParse(to, out var tp) ? tp : (DateTime?)null;
            return Results.Ok(await repo.GetByProfessionalAsync(professionalId, status, fromDt, toDt, ct));
        });

        app.MapGet("/api/appointments/mine", async (string clientId, IAppointmentRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetByClientAsync(clientId, ct)));

        app.MapGet("/api/appointments/slots", async (
            string? professionalId, string? date, IAvailabilityRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            if (string.IsNullOrWhiteSpace(date) || !System.Text.RegularExpressions.Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
                return Results.Json(new { error = "date deve ser YYYY-MM-DD" }, statusCode: 400);

            var config = await repo.GetProfessionalSchedulingConfigAsync(professionalId, ct);
            if (config is null) return Results.NotFound(new { error = "Profissional não encontrado" });

            IDictionary<string, object?> c = (IDictionary<string, object?>)(dynamic)config;
            var slotMinutes = c["slotMinutes"] is null ? 60 : Convert.ToInt32(c["slotMinutes"]);
            var leadTimeMinutes = c["leadTimeMinutes"] is null ? 0 : Convert.ToInt32(c["leadTimeMinutes"]);
            var maxAdvanceDays = c["maxAdvanceDays"] is null ? 30 : Convert.ToInt32(c["maxAdvanceDays"]);

            var parts = date.Split('-').Select(int.Parse).ToArray();
            var targetDate = new DateTime(parts[0], parts[1], parts[2], 0, 0, 0, DateTimeKind.Utc);
            var diffDays = (targetDate - DateTime.UtcNow.Date).Days;
            if (diffDays > maxAdvanceDays) return Results.Ok(Array.Empty<object>());

            // São Paulo = UTC-3
            const int SpOffsetMin = 180;
            var dayStartUtc = targetDate.AddMinutes(SpOffsetMin); // midnight SP = 03:00Z
            var dayEndUtc = dayStartUtc.AddDays(1).AddMilliseconds(-1);
            var weekday = (int)targetDate.DayOfWeek;

            var availabilities = await repo.GetAvailabilityForDayAsync(professionalId, weekday, ct);
            var appointments = await repo.GetAppointmentsForDayAsync(professionalId, dayStartUtc, dayEndUtc, ct);
            var blocks = await repo.GetBlocksForDayAsync(professionalId, dayStartUtc, dayEndUtc, ct);

            var minStart = DateTime.UtcNow.AddMinutes(leadTimeMinutes);
            var slots = new List<object>();

            foreach (var avail in availabilities)
            {
                var rangeStartUtc = dayStartUtc.AddMinutes(avail.StartMinutes);
                var rangeEndUtc = dayStartUtc.AddMinutes(avail.EndMinutes);
                var cursor = rangeStartUtc;

                while (cursor < rangeEndUtc)
                {
                    var slotEnd = cursor.AddMinutes(slotMinutes);
                    if (slotEnd > rangeEndUtc) break;
                    if (cursor < minStart) { cursor = slotEnd; continue; }

                    var hasConflict = appointments.Any(a => cursor < a.EndsAt && a.StartsAt < slotEnd)
                                   || blocks.Any(b => cursor < b.EndsAt && b.StartsAt < slotEnd);

                    if (!hasConflict) slots.Add(new { start = cursor, end = slotEnd });
                    cursor = slotEnd;
                }
            }

            return Results.Ok(slots);
        });

        app.MapPost("/api/appointments", async (
            CreateAppointmentRequest body, IAppointmentRepository repo, IEmailService email, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            if (!await repo.ProfessionalExistsAsync(body.ProfessionalId, ct))
                return Results.Json(new { error = "Profissional não encontrado." }, statusCode: 404);
            if (body.StartsAt < DateTime.UtcNow)
                return Results.Json(new { error = "Horário no passado." }, statusCode: 400);

            var (slotMinutes, allowInstantBooking) = await repo.GetProfessionalConfigAsync(body.ProfessionalId, ct);
            var endsAt = body.EndsAt == default || body.EndsAt <= body.StartsAt
                ? body.StartsAt.AddMinutes(slotMinutes ?? 60)
                : body.EndsAt;

            if (await repo.HasConflictAsync(body.ProfessionalId, body.StartsAt, endsAt, ct))
                return Results.Json(new { error = "Horário indisponível." }, statusCode: 409);

            var status = allowInstantBooking == true ? "CONFIRMED" : "PENDING";
            var created = await repo.CreateAsync(new Appointment(
                Guid.NewGuid().ToString(), body.ProfessionalId, body.ClientId, body.ServiceId,
                body.StartsAt, endsAt, status, body.Location, body.Notes), ct);

            if (status == "CONFIRMED")
                await SendBookingEmailsAsync(email, repo, created.Id, created.StartsAt, ct);

            return Results.Json(created, statusCode: 201);
        });

        app.MapPut("/api/appointments/{id}", async (
            string id, UpdateAppointmentStatusRequest body, IAppointmentRepository repo, IEmailService email, CancellationToken ct) =>
        {
            var allowed = new[] { "CONFIRMED", "CANCELLED" };
            if (!allowed.Contains(body.Status, StringComparer.OrdinalIgnoreCase))
                return Results.Json(new { error = "Status inválido." }, statusCode: 400);
            var statusUpper = body.Status.ToUpperInvariant();
            var updated = await repo.UpdateStatusAsync(id, statusUpper, ct);
            if (updated is null) return Results.NotFound(new { error = "Agendamento não encontrado." });
            if (statusUpper == "CONFIRMED")
                await SendBookingEmailsAsync(email, repo, id, updated.StartsAt, ct);
            return Results.Ok(updated);
        });

        // ─── Conversations ─────────────────────────────────────────────────────
        app.MapGet("/api/conversations", async (string? clientId, string? professionalId, IConversationRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(clientId) && string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "clientId ou professionalId é obrigatório" }, statusCode: 400);
            return Results.Ok(await repo.GetByParticipantAsync(clientId, professionalId, ct));
        });

        app.MapPost("/api/conversations", async (CreateConversationRequest body, IConversationRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ClientId) || string.IsNullOrWhiteSpace(body.ProfessionalId))
                return Results.Json(new { error = "clientId e professionalId são obrigatórios" }, statusCode: 400);
            var professionalUserId = await repo.ResolveProfessionalUserIdAsync(body.ProfessionalId, ct);
            if (professionalUserId is null)
                return Results.Json(new { error = "Profissional não encontrado" }, statusCode: 400);
            var conv = await repo.GetOrCreateAsync(body.ClientId, professionalUserId, body.OrderId, ct);
            return Results.Ok(conv);
        });

        // ─── Messages ──────────────────────────────────────────────────────────
        app.MapGet("/api/messages", async (string? conversationId, IConversationRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(conversationId))
                return Results.Json(new { error = "conversationId é obrigatório" }, statusCode: 400);
            return Results.Ok(await repo.GetMessagesAsync(conversationId, ct));
        });

        app.MapPost("/api/messages", async (SendMessageRequest body, IConversationRepository repo, IEmailService emailSvc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ConversationId) || string.IsNullOrWhiteSpace(body.SenderId) || string.IsNullOrWhiteSpace(body.Text))
                return Results.Json(new { error = "conversationId, senderId e text são obrigatórios" }, statusCode: 400);

            var message = await repo.CreateMessageAsync(body.ConversationId, body.SenderId, body.Text.Trim(), ct);
            IDictionary<string, object?> msgDict = (IDictionary<string, object?>)(dynamic)message;

            var conv = await repo.GetConversationForReadAsync(body.ConversationId, ct);
            if (conv is not null)
            {
                IDictionary<string, object?> c = (IDictionary<string, object?>)(dynamic)conv;
                var isClient = body.SenderId == c["clientId"]?.ToString();
                var lastReadAt = isClient ? c["professionalLastReadAt"] : c["clientLastReadAt"];
                var recipientEmail = isClient ? c["professionalEmail"]?.ToString() : c["clientEmail"]?.ToString();
                var recipientName = isClient ? c["professionalName"]?.ToString() ?? "Usuário" : c["clientName"]?.ToString() ?? "Usuário";
                var senderName = msgDict["senderName"]?.ToString() ?? "Usuário";

                var recentlyActive = lastReadAt is DateTime lra && (DateTime.UtcNow - lra).TotalMilliseconds <= 120_000;
                if (!recentlyActive && !string.IsNullOrWhiteSpace(recipientEmail))
                {
                    var appBaseUrl = Environment.GetEnvironmentVariable("APP_BASE_URL") ?? "https://app.doezy.com.br";
                    _ = emailSvc.SendChatMessageAsync(recipientEmail, recipientName, senderName,
                        body.Text.Trim(), $"{appBaseUrl}/chat/{body.ConversationId}",
                        body.ConversationId, windowMinutes: 10, ct).ConfigureAwait(false);
                }
            }

            return Results.Ok(message);
        });

        app.MapPost("/api/chat/read", async (MarkReadRequest body, IConversationRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ConversationId) || string.IsNullOrWhiteSpace(body.UserId))
                return Results.Json(new { error = "conversationId e userId são obrigatórios." }, statusCode: 400);
            var conv = await repo.GetConversationForReadAsync(body.ConversationId, ct);
            if (conv is null) return Results.NotFound(new { error = "Conversa não encontrada." });
            IDictionary<string, object?> c = (IDictionary<string, object?>)(dynamic)conv;
            if (body.UserId == c["clientId"]?.ToString())
                await repo.MarkReadAsync(body.ConversationId, isClient: true, ct);
            else if (body.UserId == c["professionalId"]?.ToString())
                await repo.MarkReadAsync(body.ConversationId, isClient: false, ct);
            else
                return Results.Json(new { error = "userId não pertence à conversa." }, statusCode: 403);
            return Results.Ok(new { ok = true });
        });

        // ─── Reviews ───────────────────────────────────────────────────────────
        static async Task<IResult> GetReviewsAsync(string? professionalId, int? limit, IReviewRepository repo, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            return Results.Ok(await repo.GetByProfessionalAsync(professionalId, limit ?? 10, ct));
        }

        app.MapGet("/api/reviews", GetReviewsAsync);
        app.MapGet("/reviews", GetReviewsAsync);

        app.MapPost("/api/reviews", async (CreateReviewRequest body, IReviewRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId) || string.IsNullOrWhiteSpace(body.ClientId))
                return Results.Json(new { error = "Campos obrigatórios ausentes" }, statusCode: 400);
            if (body.Rating < 1 || body.Rating > 5)
                return Results.Json(new { error = "rating deve ser 1..5" }, statusCode: 400);
            var orderId = body.OrderId?.Trim();
            if (string.IsNullOrWhiteSpace(orderId))
                return Results.Json(new { error = "orderId é obrigatório" }, statusCode: 400);
            if (!await repo.OrderBelongsToClientAsync(orderId, body.ClientId, ct))
                return Results.Json(new { error = "Pedido inválido para este cliente" }, statusCode: 400);
            if (await repo.OrderAlreadyReviewedAsync(orderId, ct))
                return Results.Json(new { error = "Este pedido já foi avaliado" }, statusCode: 400);
            return Results.Ok(await repo.CreateAsync(body.ProfessionalId, body.ClientId, orderId, body.Rating, body.Comment, ct));
        });

        app.MapGet("/api/reviews/eligible-orders", async (string? clientId, string? professionalId, IReviewRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId e clientId são obrigatórios" }, statusCode: 400);
            return Results.Ok(await repo.GetEligibleOrdersAsync(clientId, professionalId, ct));
        });

        static async Task<IResult> GetReviewByIdAsync(string id, IReviewRepository repo, CancellationToken ct)
        {
            var review = await repo.GetByIdAsync(id, ct);
            return review is null ? Results.NotFound(new { error = "Avaliação não encontrada" }) : Results.Ok(review);
        }

        app.MapGet("/api/reviews/{id}", GetReviewByIdAsync);
        app.MapGet("/reviews/{id}", GetReviewByIdAsync);

        static async Task<IResult> PatchReviewAsync(string id, UpdateReviewRequest body, IReviewRepository repo, CancellationToken ct)
        {
            var updated = await repo.UpdateAsync(id, body.Rating, body.Comment, ct);
            return updated is null ? Results.NotFound(new { error = "Avaliação não encontrada" }) : Results.Ok(updated);
        }

        app.MapMethods("/api/reviews/{id}", ["PATCH"], PatchReviewAsync);
        app.MapMethods("/reviews/{id}", ["PATCH"], PatchReviewAsync);

        // ─── Portfolio ─────────────────────────────────────────────────────────
        app.MapGet("/api/portfolio", async (string? professionalId, IPortfolioRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            return Results.Ok(await repo.GetByProfessionalAsync(professionalId, ct));
        });

        app.MapPost("/api/portfolio", async (CreatePortfolioItemRequest body, IPortfolioRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId) || string.IsNullOrWhiteSpace(body.ImageUrl))
                return Results.Json(new { error = "professionalId e imageUrl são obrigatórios" }, statusCode: 400);
            return Results.Json(await repo.CreateAsync(body.ProfessionalId, body.ImageUrl, body.Title, body.Description, ct), statusCode: 201);
        });

        app.MapGet("/api/portfolio/{id}", async (string id, IPortfolioRepository repo, CancellationToken ct) =>
        {
            var item = await repo.GetByIdAsync(id, ct);
            return item is null ? Results.NotFound(new { error = "Item não encontrado" }) : Results.Ok(item);
        });

        app.MapPut("/api/portfolio/{id}", async (string id, UpdatePortfolioItemRequest body, IPortfolioRepository repo, CancellationToken ct) =>
        {
            var updated = await repo.UpdateAsync(id, body.Title, body.Description, body.ImageUrl, body.OrderIndex, ct);
            return updated is null ? Results.NotFound(new { error = "Item não encontrado" }) : Results.Ok(updated);
        });

        app.MapDelete("/api/portfolio/{id}", async (string id, IPortfolioRepository repo, CancellationToken ct) =>
        {
            var deleted = await repo.DeleteAsync(id, ct);
            return deleted ? Results.Ok(new { ok = true }) : Results.NotFound(new { error = "Item não encontrado" });
        });

        // ─── Availability ──────────────────────────────────────────────────────
        app.MapGet("/api/pro-availability/{id}", async (string id, IAvailabilityRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetByProfessionalAsync(id, ct)));

        app.MapMethods("/api/pro-availability/{id}", ["PUT", "POST"], async (string id, SaveAvailabilityRequest body, IAvailabilityRepository repo, CancellationToken ct) =>
        {
            var rawRows = body.Items ?? body.Rows ?? [];
            var validRows = rawRows
                .Where(r => r.Weekday >= 0 && r.Weekday <= 6 && r.StartMinutes >= 0 && r.EndMinutes <= 1440 && r.EndMinutes > r.StartMinutes)
                .Select(r => (r.Weekday, r.StartMinutes, r.EndMinutes, r.Active))
                .ToList();
            if (validRows.Count == 0)
                return Results.Json(new { error = "Itens de disponibilidade inválidos" }, statusCode: 400);
            if (!await repo.ProfessionalExistsAsync(id, ct))
                return Results.NotFound(new { error = "Profissional não encontrado" });
            await repo.SaveAllAsync(id, validRows, ct);
            return Results.Ok(new { ok = true });
        });

        // ─── Professional Blocks ────────────────────────────────────────────────
        app.MapGet("/api/professional-blocks", async (string? professionalId, string? from, string? to, IAvailabilityRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(professionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            var fromDt = DateTime.TryParse(from, out var fp) ? fp : DateTime.UtcNow;
            var toDt = DateTime.TryParse(to, out var tp) ? tp : DateTime.UtcNow.AddDays(30);
            return Results.Ok(await repo.GetBlocksAsync(professionalId, fromDt, toDt, ct));
        });

        app.MapPost("/api/professional-blocks", async (CreateBlockRequest body, IAvailabilityRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId))
                return Results.Json(new { error = "professionalId é obrigatório" }, statusCode: 400);
            if (!DateTime.TryParse(body.StartsAt, out var startsAt) || !DateTime.TryParse(body.EndsAt, out var endsAt) || endsAt <= startsAt)
                return Results.Json(new { error = "Dados inválidos" }, statusCode: 400);
            return Results.Json(await repo.CreateBlockAsync(body.ProfessionalId, startsAt, endsAt, body.Reason, ct), statusCode: 201);
        });

        // ─── Order Ignores ─────────────────────────────────────────────────────
        app.MapPost("/api/order-ignores", async (CreateOrderIgnoreRequest body, IOrderIgnoreRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProfessionalId) || string.IsNullOrWhiteSpace(body.OrderId))
                return Results.Json(new { error = "professionalId e orderId são obrigatórios." }, statusCode: 400);
            if (!await repo.ProfessionalExistsAsync(body.ProfessionalId, ct))
                return Results.Json(new { error = "Profissional não encontrado." }, statusCode: 400);
            if (!await repo.OrderExistsAsync(body.OrderId, ct))
                return Results.Json(new { error = "Pedido não encontrado." }, statusCode: 400);
            await repo.UpsertAsync(body.ProfessionalId, body.OrderId, ct);
            return Results.Ok(new { ok = true });
        });

        app.MapDelete("/api/order-ignores", async (string? professionalId, string? orderId, IOrderIgnoreRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(professionalId) || string.IsNullOrWhiteSpace(orderId))
                return Results.Json(new { error = "professionalId e orderId são obrigatórios." }, statusCode: 400);
            await repo.DeleteAsync(professionalId, orderId, ct);
            return Results.Ok(new { ok = true });
        });

        return app;
    }

    // ─── Private helpers ────────────────────────────────────────────────────────
    private static async Task SendBookingEmailsAsync(IEmailService email, IAppointmentRepository repo, string appointmentId, DateTime startsAt, CancellationToken ct)
    {
        var apptData = await repo.GetAppointmentWithParticipantsAsync(appointmentId, ct);
        if (apptData is null) return;
        IDictionary<string, object?> d = (IDictionary<string, object?>)(dynamic)apptData;
        var when = startsAt.ToString("ddd, dd/MM/yyyy HH:mm");
        var appBaseUrl = Environment.GetEnvironmentVariable("APP_BASE_URL") ?? "https://app.doezy.com.br";
        var bookingUrl = $"{appBaseUrl}/agenda/{appointmentId}";
        var dedupeBase = $"booking.confirmed|{appointmentId}";
        var proEmail = d["professionalEmail"]?.ToString();
        var clientEmail = d["clientEmail"]?.ToString();
        if (!string.IsNullOrWhiteSpace(proEmail))
            _ = email.SendBookingConfirmedProfessionalAsync(proEmail,
                d["professionalName"]?.ToString() ?? "", d["clientName"]?.ToString() ?? "",
                d["serviceName"]?.ToString() ?? "Serviço", when, bookingUrl,
                dedupeKey: $"{dedupeBase}|pro|{proEmail}", ct: ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(clientEmail))
            _ = email.SendBookingConfirmedClientAsync(clientEmail,
                d["clientName"]?.ToString() ?? "", d["professionalName"]?.ToString() ?? "",
                d["serviceName"]?.ToString() ?? "Serviço", when, bookingUrl,
                dedupeKey: $"{dedupeBase}|cli|{clientEmail}", ct: ct).ConfigureAwait(false);
    }

    private static bool ShouldBypassCache(HttpRequest req)
        => req.Headers.TryGetValue("Cache-Control", out var values)
           && values.Any(v => v?.Contains("no-cache", StringComparison.OrdinalIgnoreCase) == true);

    private static async Task<IResult> GetOrSetCachedListAsync<T>(
        HttpContext context, IMemoryCache cache, string keyPrefix, TimeSpan ttl,
        Func<CancellationToken, Task<IReadOnlyList<T>>> factory, CancellationToken ct)
    {
        var cacheKey = $"{keyPrefix}:{context.Request.QueryString.Value ?? string.Empty}";
        var items = await GetOrCreateCachedAsync(cache, cacheKey, ttl, ShouldBypassCache(context.Request), () => factory(ct), logger: null, ct);
        return Results.Ok(items);
    }

    private static async Task<T> GetOrCreateCachedAsync<T>(
        IMemoryCache cache, string key, TimeSpan ttl, bool bypass,
        Func<Task<T>> factory, ILogger? logger, CancellationToken ct)
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
