using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.Extensions;

public static class WebhookEndpoints
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── POST /webhooks/mercadopago ───────────────────────────────────────
        app.MapPost("/webhooks/mercadopago", async (
            HttpRequest request,
            AppDbContext ctx,
            IPaymentRepository paymentRepo,
            IMercadoPagoService mpService,
            IConfiguration config,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("WebhookEndpoints");

            // 1. Read raw body (needed for HMAC validation and raw_payload storage)
            request.EnableBuffering();
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, ct);
            ms.Position = 0;
            request.Body.Position = 0;
            var rawPayload = Encoding.UTF8.GetString(ms.ToArray());

            // 2. Parse IPN format — v1 (query params) or v2 (JSON body)
            string? mpPaymentId = null;
            string topic = "payment";
            string? action = null;

            if (request.Query.TryGetValue("id", out var qId) && !string.IsNullOrEmpty(qId.FirstOrDefault())
                && request.Query.TryGetValue("topic", out var qTopic))
            {
                // IPN v1: ?id=98765432&topic=payment
                mpPaymentId = qId.FirstOrDefault();
                topic = qTopic.FirstOrDefault() ?? "payment";
                action = "ipn.v1";
            }
            else if (!string.IsNullOrWhiteSpace(rawPayload))
            {
                // IPN v2: JSON body
                try
                {
                    var ipn = JsonSerializer.Deserialize<MpIpnPayload>(rawPayload, _jsonOptions);
                    mpPaymentId = ipn?.Data?.Id;
                    topic = ipn?.Type ?? "payment";
                    action = ipn?.Action;
                }
                catch (JsonException)
                {
                    // Malformed body — ignore silently
                }
            }

            if (string.IsNullOrWhiteSpace(mpPaymentId))
                return Results.Ok(new { ignored = true });

            // 3. Validate HMAC-SHA256 signature
            var skipValidation =
                config.GetValue<bool>("MercadoPago__SkipWebhookSignatureValidation") ||
                config.GetValue<bool>("MercadoPago:SkipWebhookSignatureValidation");

            if (!skipValidation)
            {
                var xSignature = request.Headers["x-signature"].FirstOrDefault() ?? "";
                var xRequestId = request.Headers["x-request-id"].FirstOrDefault() ?? "";
                var webhookSecret = config["MercadoPago__WebhookSecret"] ?? config["MercadoPago:WebhookSecret"] ?? "";

                if (!ValidateMpSignature(xSignature, xRequestId, mpPaymentId, webhookSecret))
                {
                    logger.LogWarning("[Webhook] Invalid signature. MpPaymentId={MpPaymentId}", mpPaymentId);
                    return Results.Json(new { error = "invalid_signature" }, statusCode: 401);
                }
            }

            // 4. Idempotency: insert webhook_events — ON CONFLICT DO NOTHING
            var eventId = $"{mpPaymentId}:{action ?? topic}";
            var rowsInserted = await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO webhook_events (provider, event_id, topic, action, raw_payload, status, created_at)
                VALUES ('mercadopago', {eventId}, {topic}, {action}, {rawPayload}, 'received', NOW())
                ON CONFLICT (provider, event_id) DO NOTHING
                """, ct);

            if (rowsInserted == 0)
            {
                logger.LogInformation("[Webhook] Duplicate event skipped. EventId={EventId}", eventId);
                return Results.Ok(new { duplicated = true });
            }

            // 5–7. Process event — always returns 200; errors go to webhook_events.status
            string webhookStatus = "processed";
            string? errorMessage = null;

            try
            {
                var mpDetails = await mpService.GetPaymentDetailsAsync(mpPaymentId, ct);
                if (mpDetails is null)
                {
                    logger.LogWarning("[Webhook] MP payment not found. MpPaymentId={MpPaymentId}", mpPaymentId);
                    webhookStatus = "ignored";
                }
                else
                {
                    var orderId = mpDetails.ExternalReference;
                    if (string.IsNullOrWhiteSpace(orderId))
                    {
                        logger.LogWarning("[Webhook] No external_reference in MP payment. MpPaymentId={MpPaymentId}", mpPaymentId);
                        webhookStatus = "ignored";
                    }
                    else
                    {
                        logger.LogInformation(
                            "[Webhook] Processing. MpPaymentId={MpPaymentId} OrderId={OrderId} Status={Status} StatusDetail={StatusDetail}",
                            mpPaymentId, orderId, mpDetails.Status, mpDetails.StatusDetail);

                        var payment = await paymentRepo.GetByOrderIdAsync(orderId, ct);
                        if (payment is null)
                        {
                            logger.LogWarning("[Webhook] Payment not found for order. OrderId={OrderId} MpPaymentId={MpPaymentId}", orderId, mpPaymentId);
                            webhookStatus = "ignored";
                        }
                        else
                        {
                            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
                            try
                            {
                                await ProcessByStatusAsync(ctx, payment, mpDetails, orderId, ct);
                                await tx.CommitAsync(ct);
                            }
                            catch
                            {
                                await tx.RollbackAsync(ct);
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[Webhook] Processing failed. MpPaymentId={MpPaymentId} EventId={EventId}",
                    mpPaymentId, eventId);
                webhookStatus = "failed";
                errorMessage = ex.Message;
            }

            // 8. Update webhook_events status
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE webhook_events
                SET status = {webhookStatus}, error_message = {errorMessage}, processed_at = NOW()
                WHERE provider = 'mercadopago' AND event_id = {eventId}
                """, ct);

            return Results.Ok(new { ok = true });
        });

        return app;
    }

    // ─── Status processing ────────────────────────────────────────────────────

    private static async Task ProcessByStatusAsync(
        AppDbContext ctx,
        Payment payment,
        MpPaymentDetails mpDetails,
        string orderId,
        CancellationToken ct)
    {
        switch (mpDetails.Status.ToLowerInvariant())
        {
            case "approved":
                await HandleApprovedAsync(ctx, payment, mpDetails, orderId, ct);
                break;
            case "pending":
                await HandlePendingAsync(ctx, payment, mpDetails, ct);
                break;
            case "rejected":
                await HandleRejectedAsync(ctx, payment, orderId, ct);
                break;
            case "cancelled":
                await HandleCancelledAsync(ctx, payment, orderId, ct);
                break;
            case "refunded":
                await HandleRefundedAsync(ctx, payment, orderId, ct);
                break;
            case "charged_back":
                await HandleChargedBackAsync(ctx, payment, orderId, ct);
                break;
        }
    }

    private static async Task HandleApprovedAsync(
        AppDbContext ctx,
        Payment payment,
        MpPaymentDetails mpDetails,
        string orderId,
        CancellationToken ct)
    {
        var method = MapPaymentMethod(mpDetails.PaymentTypeId);
        var gatewayFeeCents = mpDetails.MpGatewayFeeCents ?? 0;
        var paidAt = mpDetails.DateApproved ?? DateTime.UtcNow;

        await ctx.Payments
            .Where(p => p.Id == payment.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, "paid")
                .SetProperty(p => p.Method, method)
                .SetProperty(p => p.GatewayFeeCents, gatewayFeeCents)
                .SetProperty(p => p.PaidAt, paidAt)
                .SetProperty(p => p.MpPaymentId, mpDetails.MpPaymentId), ct);

        // Only transition order if it is still in a pre-payment state
        await ctx.Orders
            .Where(o => o.Id == orderId &&
                (o.Status == OrderStatus.AwaitingPayment ||
                 o.Status == OrderStatus.ProposalSent ||
                 o.Status == OrderStatus.Draft ||
                 o.Status == OrderStatus.Aberto))
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Scheduled)
                .SetProperty(o => o.PaymentStatus, "paid"), ct);

        // Ledger entries
        var professionalId = await ctx.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => o.ProfessionalId)
            .FirstOrDefaultAsync(ct);

        var platformFeeCents = payment.PlatformFeeCents;
        var earningHoldCents = payment.AmountCents - platformFeeCents - gatewayFeeCents;

        if (platformFeeCents > 0)
        {
            ctx.LedgerEntries.Add(LedgerEntry.Create(
                type: "platform_fee",
                orderId: orderId,
                paymentId: payment.Id,
                professionalId: null,
                amountCents: platformFeeCents));
        }

        if (earningHoldCents > 0 && professionalId is not null)
        {
            ctx.LedgerEntries.Add(LedgerEntry.Create(
                type: "earning_hold",
                orderId: orderId,
                paymentId: payment.Id,
                professionalId: professionalId,
                amountCents: earningHoldCents));
        }

        await ctx.SaveChangesAsync(ct);
    }

    private static async Task HandlePendingAsync(
        AppDbContext ctx,
        Payment payment,
        MpPaymentDetails mpDetails,
        CancellationToken ct)
    {
        var method = MapPaymentMethod(mpDetails.PaymentTypeId);

        await ctx.Payments
            .Where(p => p.Id == payment.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, "pending")
                .SetProperty(p => p.Method, method)
                .SetProperty(p => p.MpPaymentId, mpDetails.MpPaymentId)
                .SetProperty(p => p.PixCode, mpDetails.PixCode)
                .SetProperty(p => p.PixQrCodeBase64, mpDetails.PixQrCodeBase64)
                .SetProperty(p => p.PixExpiresAt, mpDetails.PixExpiresAt), ct);
    }

    private static async Task HandleRejectedAsync(
        AppDbContext ctx,
        Payment payment,
        string orderId,
        CancellationToken ct)
    {
        await ctx.Payments
            .Where(p => p.Id == payment.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "rejected"), ct);

        await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentStatus, "failed"), ct);
    }

    private static async Task HandleCancelledAsync(
        AppDbContext ctx,
        Payment payment,
        string orderId,
        CancellationToken ct)
    {
        await ctx.Payments
            .Where(p => p.Id == payment.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "cancelled"), ct);

        await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.PaymentExpired)
                .SetProperty(o => o.PaymentStatus, "cancelled"), ct);
    }

    private static async Task HandleRefundedAsync(
        AppDbContext ctx,
        Payment payment,
        string orderId,
        CancellationToken ct)
    {
        await ctx.Payments
            .Where(p => p.Id == payment.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "refunded"), ct);

        await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Refunded)
                .SetProperty(o => o.PaymentStatus, "refunded"), ct);
    }

    private static async Task HandleChargedBackAsync(
        AppDbContext ctx,
        Payment payment,
        string orderId,
        CancellationToken ct)
    {
        await ctx.Payments
            .Where(p => p.Id == payment.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "disputed"), ct);

        await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Disputed)
                .SetProperty(o => o.PaymentStatus, "disputed"), ct);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static bool ValidateMpSignature(
        string xSignature,
        string xRequestId,
        string mpPaymentId,
        string webhookSecret)
    {
        if (string.IsNullOrEmpty(xSignature) || string.IsNullOrEmpty(webhookSecret))
            return false;

        string? ts = null, v1 = null;
        foreach (var part in xSignature.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("ts=", StringComparison.Ordinal))
                ts = trimmed.Substring(3);
            else if (trimmed.StartsWith("v1=", StringComparison.Ordinal))
                v1 = trimmed.Substring(3);
        }

        if (ts is null || v1 is null)
            return false;

        if (!long.TryParse(ts, out var tsUnix))
            return false;

        var diff = Math.Abs((DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(tsUnix)).TotalMinutes);
        if (diff > 5)
            return false;

        var template = $"id:{mpPaymentId};request-id:{xRequestId};ts:{ts};";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(template));
        var computed = Convert.ToHexString(hash).ToLowerInvariant();

        var computedBytes = Encoding.UTF8.GetBytes(computed);
        var v1Bytes = Encoding.UTF8.GetBytes(v1);
        if (computedBytes.Length != v1Bytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(computedBytes, v1Bytes);
    }

    private static string MapPaymentMethod(string? paymentTypeId) =>
        paymentTypeId?.ToLowerInvariant() switch
        {
            "pix" => "pix",
            "credit_card" => "credit_card",
            "debit_card" => "debit_card",
            "bank_transfer" => "bank_transfer",
            _ => paymentTypeId ?? "unknown"
        };

    // ─── IPN payload models ───────────────────────────────────────────────────

    private sealed class MpIpnPayload
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("live_mode")]
        public bool LiveMode { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("action")]
        public string? Action { get; init; }

        [JsonPropertyName("data")]
        public MpIpnData? Data { get; init; }
    }

    private sealed class MpIpnData
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }
}
