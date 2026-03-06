using Domain.Entities;

namespace Application.Abstractions;

public interface IConversationRepository
{
    Task<IReadOnlyList<object>> GetByParticipantAsync(string? clientId, string? professionalId, CancellationToken ct);
    Task<object?> GetOrCreateAsync(string clientId, string professionalUserId, string? orderId, CancellationToken ct);
    Task<IReadOnlyList<object>> GetMessagesAsync(string conversationId, CancellationToken ct);
    Task<object> CreateMessageAsync(string conversationId, string senderId, string text, CancellationToken ct);
    Task<object?> GetConversationForReadAsync(string conversationId, CancellationToken ct);
    Task MarkReadAsync(string conversationId, bool isClient, CancellationToken ct);
    Task<string?> ResolveProfessionalUserIdAsync(string professionalIdOrUserId, CancellationToken ct);
}
