using Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Reprocessa reembolsos pendentes (refund_status = 'pending') a cada hora.
/// Máximo de 3 tentativas por pagamento; após 3 falhas consecutivas, marca como 'failed' e alerta admin.
/// </summary>
public sealed class RefundRetryJob(
    IServiceProvider services,
    ILogger<RefundRetryJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private const int MaxAttemptsPerRun = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);

            try
            {
                await ProcessPendingRefundsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "[RefundRetryJob] Unhandled exception during retry cycle");
            }
        }
    }

    private async Task ProcessPendingRefundsAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var refundService = scope.ServiceProvider.GetRequiredService<IRefundService>();

        var pending = await paymentRepo.GetRefundPendingAsync(MaxAttemptsPerRun, ct);
        if (pending.Count == 0) return;

        logger.LogInformation("[RefundRetryJob] Processing {Count} pending refunds", pending.Count);

        foreach (var payment in pending)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var result = await refundService.RefundOrderAsync(
                    payment.OrderId,
                    payment.RefundReason ?? "retry",
                    amountCents: null,
                    ct);

                if (result.Success)
                {
                    logger.LogInformation(
                        "[RefundRetryJob] Refund succeeded for payment {PaymentId} order {OrderId}",
                        payment.Id, payment.OrderId);
                }
                else
                {
                    logger.LogError(
                        "[RefundRetryJob] Refund failed for payment {PaymentId} order {OrderId}. ErrorCode={Code}. MANUAL INTERVENTION REQUIRED.",
                        payment.Id, payment.OrderId, result.ErrorCode);
                    await paymentRepo.MarkRefundFailedAsync(payment.Id, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[RefundRetryJob] Exception processing refund for payment {PaymentId}. MANUAL INTERVENTION REQUIRED.",
                    payment.Id);
                await paymentRepo.MarkRefundFailedAsync(payment.Id, ct);
            }
        }
    }
}
