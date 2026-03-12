namespace Application.Abstractions;

public interface ITrustMetricsService
{
    /// <summary>
    /// Recalcula responseRate, avgResponseTimeMinutes e completionRate
    /// de um profissional e persiste os badges automáticos correspondentes.
    /// </summary>
    Task RecalculateAsync(string professionalId, CancellationToken ct);

    /// <summary>
    /// Recalcula métricas de todos os profissionais ativos.
    /// Chamado pelo job noturno.
    /// </summary>
    Task RecalculateAllAsync(CancellationToken ct);
}
