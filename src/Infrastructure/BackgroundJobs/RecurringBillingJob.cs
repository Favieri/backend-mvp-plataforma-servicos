using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Runs daily and generates new Orders for recurring plans whose next_billing_at &lt;= now().
///
/// Flow per due plan:
///   1. Create a RecurringOccurrence with status = 'pending'
///   2. Calculate discounted price
///   3. Create an Order (origin = 'recurring', status = 'awaiting_payment')
///   4. Mark occurrence as 'order_created', advance plan's next_billing_at
///
/// Note: In Lambda environments this job only runs on warm instances.
/// For reliable execution configure an EventBridge Scheduled Rule pointing to
/// POST /internal/jobs/recurring-billing.
/// </summary>
public sealed class RecurringBillingJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringBillingJob> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public RecurringBillingJob(IServiceScopeFactory scopeFactory, ILogger<RecurringBillingJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[RecurringBillingJob] Started. Interval: {Interval}", Interval);

        using var timer = new PeriodicTimer(Interval);

        // Run once immediately on startup, then on each daily tick
        await RunAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RecurringBillingJob] Unhandled error during tick");
            }
        }

        _logger.LogInformation("[RecurringBillingJob] Stopped.");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            var now = DateTime.UtcNow;

            var duePlans = await ctx.RecurringPlans
                .Where(p => p.Status == RecurringPlanStatus.Active && p.NextBillingAt <= now)
                .ToListAsync(ct);

            if (duePlans.Count == 0)
            {
                _logger.LogDebug("[RecurringBillingJob] No plans due at {Now}", now);
                return;
            }

            _logger.LogInformation("[RecurringBillingJob] {Count} plans due at {Now}", duePlans.Count, now);

            var processed = 0;
            var failed    = 0;

            foreach (var plan in duePlans)
            {
                var occurrenceId = Guid.NewGuid().ToString();
                var occurrence   = RecurringOccurrence.Create(
                    id:               occurrenceId,
                    planId:           plan.Id,
                    occurrenceNumber: plan.OccurrenceCount + 1,
                    scheduledFor:     plan.NextBillingAt);

                ctx.RecurringOccurrences.Add(occurrence);

                try
                {
                    var effectivePrice  = plan.EffectivePriceCents;
                    var signalCents     = (int)(effectivePrice * 0.3); // default 30% signal
                    var balanceCents    = effectivePrice - signalCents;
                    var orderId         = Guid.NewGuid().ToString();

                    var order = Order.CreateRebook(
                        id:               orderId,
                        clientId:         plan.ClientId,
                        professionalId:   plan.ProfessionalId,
                        serviceId:        plan.ServiceId,
                        tierId:           1,                 // recurring plans are tier-1 style direct
                        priceTotalCents:  effectivePrice,
                        signalCents:      signalCents,
                        balanceCents:     balanceCents,
                        installments:     1,
                        paymentMethod:    plan.PaymentMethod,
                        scope:            plan.Scope,
                        scheduledAt:      plan.NextBillingAt,
                        addressId:        plan.AddressId,
                        recurringPlanId:  plan.Id,
                        description:      $"Recorrência #{plan.OccurrenceCount + 1} — plano {plan.Id}",
                        serviceAddress:   plan.GetServiceAddress());

                    ctx.Orders.Add(order);
                    await ctx.SaveChangesAsync(ct);

                    occurrence.MarkOrderCreated(orderId);
                    plan.AdvanceBilling();
                    await ctx.SaveChangesAsync(ct);

                    processed++;

                    _logger.LogInformation(
                        "[RecurringBillingJob] Plan {PlanId} → Order {OrderId} (occurrence #{Num}, price {Price}¢, discount {Pct}%)",
                        plan.Id, orderId, occurrence.OccurrenceNumber, effectivePrice, plan.DiscountPercent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[RecurringBillingJob] Failed to generate order for plan {PlanId}", plan.Id);

                    occurrence.MarkFailed(ex.Message);
                    await ctx.SaveChangesAsync(ct);
                    failed++;
                }
            }

            _logger.LogInformation(
                "[RecurringBillingJob] Done. Processed: {Processed}, Failed: {Failed}",
                processed, failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RecurringBillingJob] Error during billing run");
        }
    }
}
