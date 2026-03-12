namespace Application.DTOs;

// Auth & Orders (existing)
public sealed record CreateOrderRequest(string ClientId, string ServiceId, string? Description, string? Location, string? Date);
public sealed record CompleteOrderRequest(string? ProfessionalId, string? ClientId);
public sealed record LoginRequest(string Email, string Senha);
public sealed record UpdateAppointmentStatusRequest(string Status);
public sealed record CreateAppointmentRequest(string ProfessionalId, string? ClientId, string? ServiceId, DateTime StartsAt, DateTime EndsAt, string? Location, string? Notes);

// Users
public sealed record CreateUserRequest(string Name, string Email, string? Phone, string Role, string Senha, string? ZoneId);

// Professionals
public sealed record CreateProfessionalRequest(string UserId, string? Bio, string[]? Zones, bool? Active);
public sealed record UpdateProfessionalRequest(string? Bio, bool? Active, string? AvailabilityText, string? AvatarUrl);

// Professional Services
public sealed record CreateProfessionalServiceRequest(string ProfessionalId, string ServiceId, string NomeServico, decimal Preco, string? Descricao);
public sealed record UpdateProfessionalServiceRequest(string? NomeServico, decimal? Preco, string? Descricao);

// Professional Zones
public sealed record UpdateProfessionalZonesRequest(string ProfessionalId, string[] Zones);

// Conversations & Messages
public sealed record CreateConversationRequest(string ClientId, string ProfessionalId, string? OrderId, string? AppointmentId);

// Phase 2: extended message with type, metadata and reply
public sealed record SendMessageRequest(
    string ConversationId,
    string SenderId,
    string Text,
    string? Type = null,        // defaults to "text" if null
    string? Metadata = null,    // JSON string for structured action payloads
    string? ReplyToId = null);  // FK to Message.id being replied to

public sealed record MarkReadRequest(string ConversationId, string UserId);

// Phase 2: update conversation status
public sealed record UpdateConversationStatusRequest(string Status);

// Phase 2: transactional chat actions sent as messages
public sealed record SendProposalMessageRequest(string ConversationId, string SenderId, string ProposalId);
public sealed record SuggestScheduleRequest(string ConversationId, string SenderId, string SuggestedDatetime, string? Note);

// Reviews
public sealed record CreateReviewRequest(string ProfessionalId, string ClientId, string? OrderId, string? AppointmentId, int Rating, string? Comment);
public sealed record UpdateReviewRequest(int? Rating, string? Comment);

// Portfolio
public sealed record CreatePortfolioItemRequest(string ProfessionalId, string ImageUrl, string? Title, string? Description);
public sealed record UpdatePortfolioItemRequest(string? Title, string? Description, string? ImageUrl, int? OrderIndex);

// Availability
public sealed record AvailabilityRow(int Weekday, int StartMinutes, int EndMinutes, bool Active);
public sealed record SaveAvailabilityRequest(AvailabilityRow[]? Items, AvailabilityRow[]? Rows);

// Blocks
public sealed record CreateBlockRequest(string ProfessionalId, string StartsAt, string EndsAt, string? Reason);

// Order Ignores
public sealed record CreateOrderIgnoreRequest(string ProfessionalId, string OrderId);

// ─── Phase 1: Order + Proposal ──────────────────────────────────────────────

// Direct booking (Tier 1)
public sealed record CreateBookingRequest(
    string ClientId,
    string ProfessionalId,
    string ServiceId,
    int TierId,
    int PriceTotalCents,
    int SignalCents,
    int BalanceCents,
    int Installments,
    string? PaymentMethod,
    string? Scope,
    string? ScheduledAt,
    string? ConversationId,
    string? AddressId,
    string? Description);

// Order from accepted proposal (Tier 2/3)
public sealed record CreateFromProposalRequest(
    string ClientId,
    int? Installments,
    string? PaymentMethod,
    string? AddressId);

// Status transition (with actor)
public sealed record UpdateOrderStatusRequest(string ActorId, string ActorRole, string NewStatus, string? Reason);

// Proposals
public sealed record CreateProposalRequest(
    string ProfessionalId,
    string ClientId,
    string ServiceId,
    string Scope,
    int PriceTotalCents,
    string ValidUntil,
    string? ProfessionalServiceId,
    string? ConversationId,
    string? IncludesDescription,
    string? ExcludesDescription,
    string? PriceByStage,
    string? DurationEstimate,
    string? SuggestedDatetime,
    int VisitFeeCents);

public sealed record SendProposalRequest(string ProfessionalId);
public sealed record AcceptProposalRequest(string ClientId, string? PaymentMethod, int? Installments);
public sealed record RejectProposalRequest(string ClientId, string? Reason);
public sealed record NegotiateProposalRequest(string ActorId, string ActorRole, string? CounterScope, int? CounterPriceCents);
