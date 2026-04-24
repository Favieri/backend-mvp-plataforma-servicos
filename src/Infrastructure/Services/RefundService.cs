using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed class RefundService(
    IPaymentRepository paymentRepo,
    IOrderRepository orderRepo,
    ILedgerRepository ledgerRepo,
    IMercadoPagoService mpService,
    ILogger<RefundService> logger) : IRefundService
{
    private const int RefundWindowDays = 179;

    public async Task<RefundResult> RefundOrderAsync(
        string orderId,
        string reason,
        int? amountCents,
        CancellationToken ct)
    {
        // 1. Buscar payment pago
        var payment = await paymentRepo.GetPaidByOrderIdAsync(orderId, ct);
        if (payment is null)
        {
            logger.LogWarning("[RefundService] No paid payment for order {OrderId}", orderId);
            return new RefundResult(false, null, 0, "no_paid_payment",
                "Não há pagamento confirmado para este pedido.");
        }

        // 2. Proteção contra double refund
        if (payment.Status == "refunded" || payment.RefundStatus == "completed")
        {
            logger.LogWarning("[RefundService] Payment {PaymentId} already refunded for order {OrderId}",
                payment.Id, orderId);
            return new RefundResult(false, null, 0, "already_refunded",
                "Este pedido já foi reembolsado.");
        }

        // 3. Verificar janela de reembolso do MP (180 dias)
        if (payment.PaidAt.HasValue)
        {
            var daysSince = (DateTime.UtcNow - payment.PaidAt.Value).TotalDays;
            if (daysSince > RefundWindowDays)
            {
                logger.LogWarning(
                    "[RefundService] Refund window expired for payment {PaymentId}. DaysSince={Days}",
                    payment.Id, daysSince);
                await paymentRepo.MarkRefundPendingAsync(payment.Id, reason, ct);
                return new RefundResult(false, null, 0, "refund_window_expired",
                    "O prazo para reembolso via Mercado Pago expirou.");
            }
        }

        if (string.IsNullOrWhiteSpace(payment.MpPaymentId))
        {
            logger.LogWarning("[RefundService] Payment {PaymentId} has no MpPaymentId", payment.Id);
            await paymentRepo.MarkRefundPendingAsync(payment.Id, reason, ct);
            return new RefundResult(false, null, 0, "gateway_error",
                "Referência do gateway não encontrada.");
        }

        // 4. Calcular valor a reembolsar
        var refundAmountCents = amountCents ?? payment.AmountCents;

        // 5. Chamar MP Refunds API
        MpRefundResult mpResult;
        try
        {
            mpResult = await mpService.RefundPaymentAsync(
                payment.MpPaymentId,
                amountCents, // null = total refund; valor = parcial
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[RefundService] MP API exception for payment {PaymentId}", payment.Id);
            await paymentRepo.MarkRefundPendingAsync(payment.Id, reason, ct);
            return new RefundResult(false, null, 0, "gateway_error",
                "Erro ao processar estorno no Mercado Pago.");
        }

        if (!mpResult.Success)
        {
            logger.LogWarning("[RefundService] MP refund failed for payment {PaymentId}. ErrorCode={Code}",
                payment.Id, mpResult.ErrorCode);
            await paymentRepo.MarkRefundPendingAsync(payment.Id, reason, ct);
            return new RefundResult(false, null, 0, mpResult.ErrorCode, "Erro ao processar estorno no Mercado Pago.");
        }

        // 6. Sucesso: atualizar payment, order e ledger
        var refundId = mpResult.RefundId ?? Guid.NewGuid().ToString();
        await paymentRepo.MarkRefundedAsync(payment.Id, refundId, reason, ct);

        // Marcar order como refunded apenas em reembolso total
        var isTotal = !amountCents.HasValue || refundAmountCents >= payment.AmountCents;
        if (isTotal)
            await orderRepo.MarkRefundedAsync(orderId, ct);

        // Ledger: entrada de refund (negativo para o profissional)
        var order = await orderRepo.GetByIdAsync(orderId, ct);
        if (order?.ProfessionalId is not null)
        {
            await ledgerRepo.AddAsync(LedgerEntry.Create(
                type: "refund",
                orderId: orderId,
                paymentId: payment.Id,
                professionalId: order.ProfessionalId,
                amountCents: -refundAmountCents), ct);

            // Cancelar earning_hold se existir: inserir earning_cancelled
            var earningCents = payment.AmountCents - payment.PlatformFeeCents - payment.GatewayFeeCents;
            if (earningCents > 0)
            {
                await ledgerRepo.AddAsync(LedgerEntry.Create(
                    type: "earning_cancelled",
                    orderId: orderId,
                    paymentId: payment.Id,
                    professionalId: order.ProfessionalId,
                    amountCents: -earningCents), ct);
            }
        }

        logger.LogInformation(
            "[RefundService] Refund completed. OrderId={OrderId} PaymentId={PaymentId} RefundId={RefundId} AmountCents={Amount} Reason={Reason}",
            orderId, payment.Id, refundId, refundAmountCents, reason);

        return new RefundResult(true, refundId, refundAmountCents, null, null);
    }
}
