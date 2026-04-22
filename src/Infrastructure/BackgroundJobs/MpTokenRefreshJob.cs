using Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Runs every 6 hours and proactively refreshes MP access tokens expiring within 7 days.
/// On HTTP 401 from MP (refresh token revoked by professional), marks account as 'expired'.
///
/// Note: In Lambda environments this job only runs on warm instances.
/// For reliable execution configure an EventBridge Scheduled Rule pointing to
/// POST /internal/jobs/mp-token-refresh.
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

        // Wait for the first tick before running to avoid cold-start pressure
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

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("[MpTokenRefreshJob] Starting token refresh sweep.");
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IProfessionalMpAccountRepository>();
            var mpService = scope.ServiceProvider.GetRequiredService<IMpOAuthService>();

            var threshold = DateTime.UtcNow.AddDays(7);
            var expiring = await repo.GetExpiringSoonAsync(threshold, ct);

            if (expiring.Count == 0)
            {
                _logger.LogDebug("[MpTokenRefreshJob] No tokens expiring before {Threshold}", threshold);
                return;
            }

            _logger.LogInformation("[MpTokenRefreshJob] {Count} token(s) to refresh before {Threshold}",
                expiring.Count, threshold);

            var refreshed = 0;
            var expired = 0;
            var failed = 0;

            foreach (var account in expiring)
            {
                try
                {
                    await repo.RefreshAndUpdateAsync(account.ProfessionalId, mpService, ct);
                    refreshed++;
                }
                catch (MpOAuthException ex) when (ex.StatusCode == 401)
                {
                    _logger.LogWarning(
                        "[MpTokenRefreshJob] Refresh token revoked for professional {ProfessionalId}. Marking expired.",
                        account.ProfessionalId);
                    await repo.MarkExpiredAsync(account.ProfessionalId, ct);
                    expired++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[MpTokenRefreshJob] Unexpected error refreshing token for professional {ProfessionalId}",
                        account.ProfessionalId);
                    failed++;
                }
            }

            _logger.LogInformation(
                "[MpTokenRefreshJob] Done. Refreshed={Refreshed} Expired={Expired} Failed={Failed}",
                refreshed, expired, failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MpTokenRefreshJob] Error during sweep");
        }
    }
}
