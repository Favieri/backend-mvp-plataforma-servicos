using Application.Abstractions;
using Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Runs every 6 hours and proactively refreshes MP OAuth tokens expiring within 7 days.
/// Uses IHostedService + PeriodicTimer. Relies on scoped services via IServiceScopeFactory.
/// Note: In Lambda environments this job only runs on warm instances. For reliable execution,
/// configure an EventBridge Scheduled Rule pointing to a dedicated Lambda function or migrate to ECS.
/// </summary>
public sealed class MpTokenRefreshJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MpTokenRefreshJob> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public MpTokenRefreshJob(IServiceScopeFactory scopeFactory, ILogger<MpTokenRefreshJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[MpTokenRefreshJob] Started. Interval: {Interval}", Interval);

        using var timer = new PeriodicTimer(Interval);

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
                _logger.LogError(ex, "[MpTokenRefreshJob] Unhandled error during tick");
            }
        }

        _logger.LogInformation("[MpTokenRefreshJob] Stopped.");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var mpRepo = scope.ServiceProvider.GetRequiredService<IProfessionalMpAccountRepository>();
        var mpService = scope.ServiceProvider.GetRequiredService<IMpOAuthService>();

        try
        {
            var expiring = await mpRepo.GetExpiringTokensAsync(7, ct);

            if (expiring.Count == 0)
            {
                _logger.LogDebug("[MpTokenRefreshJob] No tokens expiring within 7 days.");
                return;
            }

            _logger.LogInformation("[MpTokenRefreshJob] Found {Count} tokens to refresh.", expiring.Count);

            var succeeded = 0;
            var failed = 0;

            foreach (var account in expiring)
            {
                try
                {
                    await mpService.GetValidAccessTokenAsync(account.ProfessionalId, ct);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "[MpTokenRefreshJob] Failed to refresh token for professional {ProfessionalId}", account.ProfessionalId);
                }
            }

            _logger.LogInformation("[MpTokenRefreshJob] Refresh complete. Succeeded={Succeeded} Failed={Failed}", succeeded, failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MpTokenRefreshJob] Error during refresh run");
        }
    }
}
