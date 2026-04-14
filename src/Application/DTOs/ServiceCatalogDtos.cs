namespace Application.DTOs;

public record TierDto(
    int Id,
    string Name,
    string Code,
    bool AllowBookingDirect,
    bool RequiresProposal,
    bool RequiresChat,
    string[] AllowedPriceFormats);

public record CategoryDto(
    string Id,
    string Name,
    string? Icon);
