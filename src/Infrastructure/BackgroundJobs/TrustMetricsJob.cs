using Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Job diário que recalcula responseRate, avgResponseTimeMinutes, completionRate
/// e badges automáticos para todos os profissionais ativos.
///
/// Intervalo padrão: 24 horas.
/// Em ambientes Lambda, pode ser acionado via EventBridge + POST /internal/jobs/trust-metrics.
/// </summary>
public sealed class TrustMetricsJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrustMetricsJob> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public TrustMetricsJob(IServiceScopeFactory scopeFactory, ILogger<TrustMetricsJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[TrustMetricsJob] Started. Interval: {Interval}", Interval);

        using var timer = new PeriodicTimer(Interval);

        // Aguarda o primeiro tick para não sobrecarregar na inicialização
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
        }
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("[TrustMetricsJob] Iniciando recálculo de métricas de confiança.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ITrustMetricsService>();
            await service.RecalculateAllAsync(ct);
            _logger.LogInformation("[TrustMetricsJob] Recálculo concluído.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TrustMetricsJob] Erro durante o recálculo de métricas.");
        }
    }
}
