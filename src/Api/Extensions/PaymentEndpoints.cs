using Api.Security;
using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

// RefundResult alias to avoid ambiguity with local record
using RefundResult = Application.Abstractions.RefundResult;

namespace Api.Extensions;

public static class PaymentEndpoints
{
    private static readonly HashSet<string> PayableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        OrderStatus.AwaitingPayment,
        OrderStatus.ProposalSent,
        OrderStatus.Draft
    };

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── POST /payments/preference ───────────────────────────────────────────
        app.MapPost("/payments/preference", async (
            CreatePaymentPreferenceRequest body,
            HttpContext context,
            AppDbContext ctx,
            IPaymentRepository paymentRepo,
            IMercadoPagoService mpService,
            IMpOAuthService mpOAuth,
            IConfiguration config,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("PaymentEndpoints");

            if (string.IsNullOrWhiteSpace(body.OrderId))
                return Results.Json(new { error = "orderId é obrigatório" }, statusCode: 400);

            var jwtUserId = AuthorizationHelpers.GetJwtUserId(context);
            if (jwtUserId is null)
                return Results.Json(new { error = "Autenticação necessária" }, statusCode: 401);

            // 1. Buscar e validar o pedido
            var order = await ctx.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == body.OrderId, ct);

            if (order is null || !PayableStatuses.Contains(order.Status))
                return Results.Json(
                    new { error = "order_not_payable", message = "Pedido não está disponível para pagamento." },
                    statusCode: 422);

            if (order.ClientId != jwtUserId && !AuthorizationHelpers.IsAdmin(context))
                return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

            if (order.ProfessionalId is null)
                return Results.Json(
                    new { error = "order_not_payable", message = "Pedido não está vinculado a um profissional." },
                    statusCode: 422);

            // 2. Verificar conexão do profissional com MP
            var professional = await ctx.Professionals.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == order.ProfessionalId, ct);

            if (professional is null || !professional.MpConnected)
                return Results.Json(
                    new { error = "professional_mp_not_connected", message = "Profissional ainda não conectou sua conta de recebimento." },
                    statusCode: 422);

            // 3. Proteção contra pagamento duplicado
            var existingPending = await paymentRepo.GetPendingByOrderIdAsync(body.OrderId, ct);
            if (existingPending is not null)
            {
                var age = DateTime.UtcNow - existingPending.CreatedAt;
                var remainingMinutes = 60 - age.TotalMinutes; // preference TTL é 60 min

                if (remainingMinutes >= 10) // só reutiliza se sobram pelo menos 10 min de validade
                {
                    // Retorna o mesmo preferenceId sem chamar o MP novamente
                    var amountReuse = existingPending.AmountCents;
                    var feeReuse = existingPending.PlatformFeeCents;
                    var isSandboxReuse = config.GetValue<bool>("MercadoPago__IsSandbox")
                                     || config.GetValue<bool>("MercadoPago:IsSandbox");
                    var reuseCheckout = existingPending.GatewayRef is not null
                        ? $"https://www.mercadopago.com.br/checkout/v1/redirect?pref_id={existingPending.GatewayRef}"
                        : null;
                    var reuseSandbox = existingPending.GatewayRef is not null
                        ? $"https://sandbox.mercadopago.com.br/checkout/v1/redirect?pref_id={existingPending.GatewayRef}"
                        : null;
                    return Results.Ok(new
                    {
                        paymentId = existingPending.Id,
                        preferenceId = existingPending.GatewayRef,
                        checkoutUrl = isSandboxReuse ? reuseSandbox : reuseCheckout,
                        sandboxUrl = reuseSandbox,
                        amountCents = amountReuse,
                        platformFeeCents = feeReuse,
                        netAmountCents = amountReuse - feeReuse,
                        expiresAt = (DateTime?)null
                    });
                }

                // Preference expirada ou prestes a expirar — cancela o pendente anterior e cria um novo
                await paymentRepo.CancelPendingByOrderIdAsync(body.OrderId, ct);
                logger.LogInformation(
                    "[PaymentEndpoints] Cancelled stale pending payment for order {OrderId} (age: {Age:F1} min)",
                    body.OrderId, age.TotalMinutes);
            }

            // 4. Calcular valores e taxas por tier
            var tierId = order.TierId ?? 1;
            var platformFeePercent = config
                .GetSection("MercadoPago:PlatformFeeByTier")
                .GetValue<double>($"{tierId}");
            if (platformFeePercent <= 0) platformFeePercent = 10.0;

            var maxInstallments = config
                .GetSection("MercadoPago:MaxInstallmentsByTier")
                .GetValue<int>($"{tierId}");
            if (maxInstallments <= 0) maxInstallments = 6;

            var amountCents = order.SignalCents ?? order.PriceTotalCents ?? 0;
            if (amountCents <= 0)
                return Results.Json(
                    new { error = "order_not_payable", message = "Pedido não possui valor definido para pagamento." },
                    statusCode: 422);

            var platformFeeCents = (int)Math.Round(amountCents * platformFeePercent / 100.0);

            if (platformFeeCents > amountCents * 0.20)
                return Results.Json(
                    new { error = "fee_exceeds_limit", message = "Taxa da plataforma excede o limite permitido pelo Mercado Pago (20%)." },
                    statusCode: 422);

            var netAmountCents = amountCents - platformFeeCents;

            // 5. Buscar nome do serviço
            var serviceName = await ctx.Services.AsNoTracking()
                .Where(s => s.Id == order.ServiceId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(ct) ?? "Serviço";

            // 6. Obter access token do profissional
            string professionalAccessToken;
            try
            {
                professionalAccessToken = await mpOAuth.GetValidAccessTokenAsync(order.ProfessionalId, ct);
            }
            catch (MpAccountNotConnectedException)
            {
                return Results.Json(
                    new { error = "professional_mp_not_connected", message = "Profissional ainda não conectou sua conta de recebimento." },
                    statusCode: 422);
            }

            // 7. Montar URLs de retorno
            var frontendUrl = config["MercadoPago__FrontendBaseUrl"] ?? config["MercadoPago:FrontendBaseUrl"] ?? "";
            var apiBaseUrl = config["MercadoPago__ApiBaseUrl"] ?? config["MercadoPago:ApiBaseUrl"] ?? "";
            var orderId = body.OrderId;

            var preferenceRequest = new CreatePreferenceRequest(
                OrderId: orderId,
                ServiceName: serviceName,
                AmountCents: amountCents,
                PlatformFeeCents: platformFeeCents,
                MaxInstallments: maxInstallments,
                PayerEmail: body.PayerEmail,
                PayerCpf: body.PayerCpf,
                BackUrlSuccess: $"{frontendUrl}/pagamento/sucesso?orderId={orderId}",
                BackUrlFailure: $"{frontendUrl}/pagamento/falhou?orderId={orderId}",
                BackUrlPending: $"{frontendUrl}/pagamento/pendente?orderId={orderId}",
                NotificationUrl: $"{apiBaseUrl}/webhooks/mercadopago"
            );

            // 8. Criar preference no MP
            MpPreferenceResult mpResult;
            try
            {
                mpResult = await mpService.CreatePreferenceAsync(preferenceRequest, professionalAccessToken, ct);
            }
            catch (MpGatewayException ex)
            {
                logger.LogError(ex, "[PaymentEndpoints] MP gateway error for order {OrderId}", orderId);
                return Results.Json(
                    new { error = "gateway_error", message = "Erro ao criar preferência de pagamento. Tente novamente." },
                    statusCode: 502);
            }

            // 9. Salvar registro na tabela payment
            var payment = Payment.CreateForMercadoPago(
                id: Guid.NewGuid().ToString(),
                orderId: orderId,
                gatewayRef: mpResult.PreferenceId,
                amountCents: amountCents,
                platformFeeCents: platformFeeCents);

            await paymentRepo.CreateAsync(payment, ct);

            // 10. Atualizar Order com preferenceId e taxas
            await ctx.Orders
                .Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.MpPreferenceId, mpResult.PreferenceId)
                    .SetProperty(o => o.PlatformFeePercent, (decimal)platformFeePercent)
                    .SetProperty(o => o.PlatformFeeCents, platformFeeCents),
                ct);

            logger.LogInformation(
                "[PaymentEndpoints] Preference created. OrderId={OrderId} PaymentId={PaymentId} PreferenceId={PreferenceId} AmountCents={Amount} FeeCents={Fee}",
                orderId, payment.Id, mpResult.PreferenceId, amountCents, platformFeeCents);

            return Results.Ok(new
            {
                paymentId = payment.Id,
                preferenceId = mpResult.PreferenceId,
                checkoutUrl = mpResult.IsSandbox ? mpResult.SandboxUrl : mpResult.CheckoutUrl,
                sandboxUrl = mpResult.SandboxUrl,
                amountCents,
                platformFeeCents,
                netAmountCents,
                expiresAt = mpResult.ExpiresAt
            });
        });

        // ─── GET /payments/{orderId} ─────────────────────────────────────────────
        app.MapGet("/payments/{orderId}", async (
            string orderId,
            IPaymentRepository paymentRepo,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var payment = await paymentRepo.GetByOrderIdAsync(orderId, ct);
            if (payment is null)
                return Results.NotFound(new { error = "Pagamento não encontrado para este pedido." });

            var isSandbox = config.GetValue<bool>("MercadoPago__IsSandbox")
                         || config.GetValue<bool>("MercadoPago:IsSandbox");

            string? checkoutUrl = payment.GatewayRef is not null
                ? $"https://www.mercadopago.com.br/checkout/v1/redirect?pref_id={payment.GatewayRef}"
                : null;

            string? sandboxUrl = payment.GatewayRef is not null
                ? $"https://sandbox.mercadopago.com.br/checkout/v1/redirect?pref_id={payment.GatewayRef}"
                : null;

            return Results.Ok(new
            {
                paymentId = payment.Id,
                orderId = payment.OrderId,
                status = payment.Status,
                method = payment.Method == "unknown" ? (string?)null : payment.Method,
                amountCents = payment.AmountCents,
                platformFeeCents = payment.PlatformFeeCents,
                gatewayFeeCents = payment.GatewayFeeCents,
                paidAt = payment.PaidAt,
                preferenceId = payment.GatewayRef,
                checkoutUrl = isSandbox ? sandboxUrl : checkoutUrl,
                sandboxUrl,
                pixCode = payment.PixCode,
                pixQrCodeBase64 = payment.PixQrCodeBase64,
                pixExpiresAt = payment.PixExpiresAt
            });
        });

        // ─── POST /payments/{orderId}/refund ─────────────────────────────────────
        // Auth: admin/sistema interno. Chamado internamente pelo fluxo de cancelamento ou disputa.
        app.MapPost("/payments/{orderId}/refund", async (
            string orderId,
            RefundOrderRequest body,
            HttpContext context,
            IRefundService refundService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (AuthorizationHelpers.RequireAdmin(context) is { } authError)
                return authError;

            var logger = loggerFactory.CreateLogger("PaymentEndpoints");

            if (string.IsNullOrWhiteSpace(body.Reason))
                return Results.Json(new { error = "reason é obrigatório" }, statusCode: 400);

            RefundResult result;
            try
            {
                result = await refundService.RefundOrderAsync(orderId, body.Reason, body.AmountCents, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[PaymentEndpoints] Refund exception for order {OrderId}", orderId);
                return Results.Json(
                    new { error = "gateway_error", message = "Erro ao processar estorno no Mercado Pago." },
                    statusCode: 502);
            }

            if (!result.Success)
            {
                var statusCode = result.ErrorCode switch
                {
                    "no_paid_payment" or "already_refunded" or "refund_window_expired" => 422,
                    _ => 502
                };
                return Results.Json(new { error = result.ErrorCode, message = result.ErrorMessage }, statusCode: statusCode);
            }

            return Results.Ok(new
            {
                ok = true,
                refundId = result.RefundId,
                amountCents = result.AmountCents,
                orderId,
                status = "refunded"
            });
        });

        // ─── POST /payments/{paymentId}/cancel ───────────────────────────────────
        app.MapPost("/payments/{paymentId}/cancel", async (
            string paymentId,
            HttpContext context,
            AppDbContext ctx,
            IPaymentRepository paymentRepo,
            CancellationToken ct) =>
        {
            var jwtUserId = context.User?.FindFirst("sub")?.Value
                ?? context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (jwtUserId is null)
                return Results.Json(new { error = "Autenticação necessária" }, statusCode: 401);

            var payment = await paymentRepo.GetByIdAsync(paymentId, ct);
            if (payment is null)
                return Results.NotFound(new { error = "Pagamento não encontrado." });

            if (payment.Status != "pending")
                return Results.Json(
                    new { error = "payment_not_cancellable", message = $"Pagamento com status '{payment.Status}' não pode ser cancelado." },
                    statusCode: 422);

            await paymentRepo.UpdateStatusAsync(paymentId, "cancelled", ct);

            await ctx.Orders
                .Where(o => o.Id == payment.OrderId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, OrderStatus.CancelledClient)
                    .SetProperty(o => o.CancelledAt, DateTime.UtcNow)
                    .SetProperty(o => o.CancelledBy, "client"),
                ct);

            return Results.Ok(new { ok = true });
        });

        return app;
    }
}

public sealed record CreatePaymentPreferenceRequest(
    string? OrderId,
    string? PayerEmail,
    string? PayerCpf
);

public sealed record RefundOrderRequest(
    string Reason,
    int? AmountCents,
    string? Note
);
