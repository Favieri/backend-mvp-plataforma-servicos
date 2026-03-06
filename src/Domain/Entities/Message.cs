namespace Domain.Entities;

public sealed record Message(
    string Id,
    string ConversationId,
    string SenderId,
    string Text,
    DateTime SentAt);
