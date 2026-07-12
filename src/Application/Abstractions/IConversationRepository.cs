using Application.Services;
using Domain.Entities;

namespace Application.Abstractions;

public interface IConversationRepository
{
    Task<IReadOnlyList<object>> GetByParticipantAsync(string? clientId, string? professionalId, CancellationToken ct);
    Task<object?> GetOrCreateAsync(string clientId, string professionalUserId, string? orderId, CancellationToken ct);
    Task<IReadOnlyList<object>> GetMessagesAsync(string conversationId, CancellationToken ct, DateTime? since = null);

    /// <summary>Phase 2: create message with type, metadata and optional replyToId.</summary>
    Task<object> CreateMessageAsync(
        string conversationId,
        string senderId,
        string text,
        string type,
        string? metadata,
        string? replyToId,
        CancellationToken ct);

    Task<object?> GetConversationForReadAsync(string conversationId, CancellationToken ct);
    Task MarkReadAsync(string conversationId, bool isClient, CancellationToken ct);
    Task<string?> ResolveProfessionalUserIdAsync(string professionalIdOrUserId, CancellationToken ct);

    // Phase 2: conversation status management
    Task UpdateConversationStatusAsync(string conversationId, string status, CancellationToken ct);

    // Phase 2: get available transactional actions for a conversation
    Task<object> GetConversationActionsAsync(string conversationId, string requestingUserId, CancellationToken ct);

    /// <summary>Candidatos a e-mail de silêncio: conversas cuja última mensagem tem mais de silenceThreshold e ainda pode estar não lida.</summary>
    Task<IReadOnlyList<ChatSilenceCandidate>> GetChatSilenceCandidatesAsync(TimeSpan silenceThreshold, CancellationToken ct);

    Task UpsertChatNotificationStateAsync(string conversationId, string recipientUserId, string messageId, CancellationToken ct);
}
