using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Runs every hour and expires proposals where valid_until &lt; now() and status = 'sent'.
/// Uses IHostedService + PeriodicTimer. Relies on scoped DbContext via IServiceScopeFactory.
/// Note: In Lambda environments this job only runs on warm instances. For reliable execution,
/// configure an EventBridge Scheduled Rule pointing to a dedicated Lambda function or migrate to ECS.
/// </summary>
public sealed class ProposalExpirationJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProposalExpirationJob> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public ProposalExpirationJob(IServiceScopeFactory scopeFactory, ILogger<ProposalExpirationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ProposalExpirationJob] Started. Interval: {Interval}", Interval);

        using var timer = new PeriodicTimer(Interval);

        // Run once immediately on startup, then on each tick
        try
        {
            await RunAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProposalExpirationJob] Unhandled error during initial run");
        }

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
                _logger.LogError(ex, "[ProposalExpirationJob] Unhandled error during tick");
            }
        }

        _logger.LogInformation("[ProposalExpirationJob] Stopped.");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        try
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var now = DateTime.UtcNow;

            var expired = await ctx.Proposals
                .Where(p => p.Status == Domain.Enums.ProposalStatus.Sent && p.ValidUntil < now)
                .ToListAsync(ct);

            if (expired.Count == 0)
            {
                _logger.LogDebug("[ProposalExpirationJob] No proposals to expire at {Now}", now);
                return;
            }

            foreach (var proposal in expired)
                proposal.Expire();

            await ctx.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[ProposalExpirationJob] Expired {Count} proposals at {Now}",
                expired.Count, now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProposalExpirationJob] Error during expiration run");
        }
    }
}
