namespace Domain.Entities;

public sealed record WebhookEvent(
    string Provider,
    string EventId,
    string Topic,
    string? Action,
    string RawPayload,
    string Status,               // received | processed | failed | ignored
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? ProcessedAt);
