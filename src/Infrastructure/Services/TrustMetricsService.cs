using Application.Abstractions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Calcula e persiste métricas de confiança e badges automáticos para profissionais.
///
/// Métricas calculadas:
///   - responseRate: % de conversas em que o profissional respondeu ao menos uma mensagem
///   - avgResponseTimeMinutes: tempo médio (em minutos) da primeira resposta do profissional
///   - completionRate: % de pedidos confirmados que chegaram ao status completed/evaluated
///
/// Badges automáticos:
///   - "verified": verificationStatus = 'verified'
///   - "top_pro": rating >= 4.8 e completionRate >= 0.95 e pelo menos 10 pedidos concluídos
///   - "responsive": responseRate >= 0.90 e avgResponseTimeMinutes <= 60
/// </summary>
public sealed class TrustMetricsService(AppDbContext ctx, ILogger<TrustMetricsService> logger) : ITrustMetricsService
{
    private const double TopProMinRating = 4.8;
    private const double TopProMinCompletionRate = 0.95;
    private const int TopProMinCompletedJobs = 10;
    private const double ResponsiveBadgeMinRate = 0.90;
    private const int ResponsiveBadgeMaxMinutes = 60;

    public async Task RecalculateAsync(string professionalId, CancellationToken ct)
    {
        logger.LogInformation("[TrustMetrics] Recalculando métricas para profissional {ProfessionalId}", professionalId);

        var professional = await ctx.Professionals
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == professionalId, ct);

        if (professional is null)
        {
            logger.LogWarning("[TrustMetrics] Profissional {ProfessionalId} não encontrado.", professionalId);
            return;
        }

        // Conversation.ProfessionalId é o UserId do profissional (FK -> User.id)
        var userId = professional.UserId;

        var (responseRate, avgResponseTimeMinutes) = await CalculateResponseMetricsAsync(professionalId, userId, ct);
        var completionRate = await CalculateCompletionRateAsync(professionalId, ct);

        var badges = BuildBadges(
            verificationStatus: professional.VerificationStatus,
            rating: professional.Rating,
            completedJobsCount: professional.CompletedJobsCount,
            responseRate: responseRate,
            avgResponseTimeMinutes: avgResponseTimeMinutes,
            completionRate: completionRate);

        await ctx.Professionals
            .Where(p => p.Id == professionalId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ResponseRate, responseRate)
                .SetProperty(p => p.AvgResponseTimeMinutes, avgResponseTimeMinutes)
                .SetProperty(p => p.CompletionRate, completionRate)
                .SetProperty(p => p.Badges, badges.Count > 0 ? string.Join(",", badges) : null),
            ct);

        logger.LogInformation(
            "[TrustMetrics] Profissional {ProfessionalId} atualizado — responseRate={ResponseRate:P1}, completionRate={CompletionRate:P1}, badges=[{Badges}]",
            professionalId, responseRate, completionRate, string.Join(",", badges));
    }

    public async Task RecalculateAllAsync(CancellationToken ct)
    {
        var professionalIds = await ctx.Professionals
            .AsNoTracking()
            .Where(p => p.Active)
            .Select(p => p.Id)
            .ToListAsync(ct);

        logger.LogInformation("[TrustMetrics] Recalculando métricas para {Count} profissionais.", professionalIds.Count);

        foreach (var id in professionalIds)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await RecalculateAsync(id, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[TrustMetrics] Erro ao recalcular métricas para {ProfessionalId}.", id);
            }
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <param name="professionalId">ID do perfil profissional (Professional.Id)</param>
    /// <param name="userId">UserId do profissional (Conversation.ProfessionalId referencia User.Id)</param>
    private async Task<(double? responseRate, int? avgResponseTimeMinutes)> CalculateResponseMetricsAsync(
        string professionalId, string userId, CancellationToken ct)
    {
        // Conversas onde o profissional participa (referenciado por UserId)
        var conversations = await ctx.Conversations
            .AsNoTracking()
            .Where(c => c.ProfessionalId == userId)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (conversations.Count == 0)
            return (null, null);

        var responseTimes = new List<int>();
        int respondedCount = 0;

        foreach (var convId in conversations)
        {
            // Primeira mensagem de alguém que não é o profissional (pelo UserId)
            var firstClientMessage = await ctx.Messages
                .AsNoTracking()
                .Where(m => m.ConversationId == convId && m.SenderId != userId)
                .OrderBy(m => m.SentAt)
                .Select(m => new { m.SentAt })
                .FirstOrDefaultAsync(ct);

            if (firstClientMessage is null) continue;

            // Primeira resposta do profissional após a mensagem do cliente
            var firstProResponse = await ctx.Messages
                .AsNoTracking()
                .Where(m => m.ConversationId == convId
                         && m.SenderId == userId
                         && m.SentAt > firstClientMessage.SentAt)
                .OrderBy(m => m.SentAt)
                .Select(m => new { m.SentAt })
                .FirstOrDefaultAsync(ct);

            if (firstProResponse is not null)
            {
                respondedCount++;
                var minutes = (int)(firstProResponse.SentAt - firstClientMessage.SentAt).TotalMinutes;
                if (minutes >= 0) responseTimes.Add(minutes);
            }
        }

        var responseRate = (double)respondedCount / conversations.Count;
        var avgResponseTimeMinutes = responseTimes.Count > 0
            ? (int?)responseTimes.Average()
            : null;

        return (Math.Round(responseRate, 4), avgResponseTimeMinutes);
    }

    private async Task<double?> CalculateCompletionRateAsync(string professionalId, CancellationToken ct)
    {
        // Pedidos que chegaram à fase de execução (scheduled ou além)
        var startedStatuses = new[]
        {
            "scheduled", "in_progress", "awaiting_confirmation",
            "completed", "evaluated", "disputed", "refunded"
        };

        var totalStarted = await ctx.Orders
            .AsNoTracking()
            .CountAsync(o => o.ProfessionalId == professionalId
                          && startedStatuses.Contains(o.Status), ct);

        if (totalStarted == 0)
            return null;

        var completedStatuses = new[] { "completed", "evaluated" };

        var totalCompleted = await ctx.Orders
            .AsNoTracking()
            .CountAsync(o => o.ProfessionalId == professionalId
                          && completedStatuses.Contains(o.Status), ct);

        return Math.Round((double)totalCompleted / totalStarted, 4);
    }

    private static List<string> BuildBadges(
        string verificationStatus,
        double? rating,
        int completedJobsCount,
        double? responseRate,
        int? avgResponseTimeMinutes,
        double? completionRate)
    {
        var badges = new List<string>();

        if (verificationStatus == "verified")
            badges.Add("verified");

        if (rating >= TopProMinRating
            && completionRate >= TopProMinCompletionRate
            && completedJobsCount >= TopProMinCompletedJobs)
        {
            badges.Add("top_pro");
        }

        if (responseRate >= ResponsiveBadgeMinRate
            && avgResponseTimeMinutes.HasValue
            && avgResponseTimeMinutes.Value <= ResponsiveBadgeMaxMinutes)
        {
            badges.Add("responsive");
        }

        return badges;
    }
}
