using Application.Abstractions;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Runs every 15 minutes and notifies a conversation's recipient by e-mail once their unread
/// message has been sitting for more than SilenceThreshold (2h), instead of the previous
/// synchronous "not recently active" trigger fired directly on POST /messages.
/// Uses IHostedService + PeriodicTimer, same pattern as ProposalExpirationJob.
/// Note: In Lambda environments this job only runs on warm instances. For reliable execution,
/// configure an EventBridge Scheduled Rule pointing to a dedicated Lambda function or migrate to ECS.
/// </summary>
public sealed class ChatSilenceNotificationJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatSilenceNotificationJob> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public ChatSilenceNotificationJob(IServiceScopeFactory scopeFactory, ILogger<ChatSilenceNotificationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ChatSilenceNotificationJob] Started. Interval: {Interval}", Interval);

        using var timer = new PeriodicTimer(Interval);

        try
        {
            await RunOnceAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatSilenceNotificationJob] Unhandled error during initial run");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatSilenceNotificationJob] Unhandled error during tick");
            }
        }

        _logger.LogInformation("[ChatSilenceNotificationJob] Stopped.");
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        try
        {
            var repo = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
            var emailSvc = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var now = DateTime.UtcNow;
            var candidates = await repo.GetChatSilenceCandidatesAsync(ChatSilenceRules.SilenceThreshold, ct);

            var notified = 0;
            var appBaseUrl = Environment.GetEnvironmentVariable("APP_BASE_URL") ?? "https://jobeasy.com.br";

            foreach (var candidate in candidates)
            {
                if (!ChatSilenceRules.IsEligible(candidate, now)) continue;

                var recipientEmail = candidate.RecipientEmail;
                if (string.IsNullOrWhiteSpace(recipientEmail)) continue;

                await emailSvc.SendChatMessageAsync(
                    recipientEmail,
                    candidate.RecipientName,
                    candidate.SenderName,
                    candidate.MessageText,
                    $"{appBaseUrl}/chat/{candidate.ConversationId}",
                    candidate.ConversationId,
                    windowMinutes: 10,
                    ct);

                await repo.UpsertChatNotificationStateAsync(candidate.ConversationId, candidate.RecipientUserId, candidate.MessageId, ct);
                notified++;
            }

            _logger.LogInformation(
                "[ChatSilenceNotificationJob] Checked {Count} candidates, notified {Notified} at {Now}",
                candidates.Count, notified, now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatSilenceNotificationJob] Error during run");
        }
    }
}
