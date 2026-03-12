namespace Domain.Entities;

public sealed record Message(
    string Id,
    string ConversationId,
    string SenderId,
    string Text,
    DateTime SentAt,
    // Phase 2: transactional chat
    string Type = "text",
    string? Metadata = null,
    string? ReplyToId = null);
