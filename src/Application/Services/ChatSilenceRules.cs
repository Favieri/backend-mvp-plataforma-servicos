namespace Application.Services;

/// <summary>Snapshot of a conversation's last message plus recipient/notification state, used by ChatSilenceRules.IsEligible.</summary>
public sealed record ChatSilenceCandidate(
    string ConversationId,
    string RecipientUserId,
    string? RecipientEmail,
    string RecipientName,
    string SenderName,
    string MessageId,
    string MessageText,
    DateTime MessageSentAt,
    DateTime? RecipientLastReadAt,
    string? LastNotifiedMessageId);

public static class ChatSilenceRules
{
    public static readonly TimeSpan SilenceThreshold = TimeSpan.FromHours(2);
    public static readonly TimeSpan RecentActivityThreshold = TimeSpan.FromMinutes(2);

    /// <summary>
    /// A conversa é elegível para e-mail de silêncio quando: a última mensagem não foi lida
    /// pelo destinatário, está parada há mais de SilenceThreshold, ainda não foi notificada
    /// para esta mensagem específica, e o destinatário não esteve ativo na conversa recentemente.
    /// </summary>
    public static bool IsEligible(ChatSilenceCandidate candidate, DateTime now)
    {
        var isUnread = candidate.RecipientLastReadAt is null || candidate.RecipientLastReadAt < candidate.MessageSentAt;
        if (!isUnread) return false;

        if (now - candidate.MessageSentAt < SilenceThreshold) return false;

        if (candidate.LastNotifiedMessageId == candidate.MessageId) return false;

        var recentlyActive = candidate.RecipientLastReadAt is DateTime lastReadAt
            && (now - lastReadAt) <= RecentActivityThreshold;
        if (recentlyActive) return false;

        return true;
    }
}
