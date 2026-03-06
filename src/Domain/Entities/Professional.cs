namespace Domain.Entities;

public sealed record Professional(
    string Id,
    string UserId,
    string? Bio,
    double? Rating,
    bool Active,
    string? AvatarUrl,
    string? AvailabilityText,
    int CompletedJobsCount,
    int? SlotMinutes,
    int? LeadTimeMinutes,
    int? MaxAdvanceDays,
    bool? AllowInstantBooking);
