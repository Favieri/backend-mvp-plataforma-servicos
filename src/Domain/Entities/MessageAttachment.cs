namespace Domain.Entities;

public sealed record MessageAttachment(
    string Id,
    string MessageId,
    string Type,
    string Url,
    string? ThumbnailUrl,
    string? FileName,
    int? SizeBytes,
    DateTime CreatedAt);
