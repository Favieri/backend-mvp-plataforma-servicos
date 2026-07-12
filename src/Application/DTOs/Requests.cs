namespace Application.DTOs;

// ─── User Address (múltiplos endereços por usuário — PRD-18a) ────────────────
public sealed record UserAddressDto(
    string Id,
    string? Label,
    string ZipCode,
    string Street,
    string Number,
    string Neighborhood,
    string City,
    string State,
    string? Complement,
    string? Reference,
    bool IsDefault,
    DateTime? LastUsedAt);

public sealed record CreateUserAddressRequest(
    string? Label,
    string ZipCode,
    string Street,
    string Number,
    string Neighborhood,
    string City,
    string State,
    string? Complement,
    string? Reference,
    bool SetAsDefault = false);

// ─── Address ────────────────────────────────────────────────────────────────
public sealed record AddressDto(
    string ZipCode,
    string Street,
    string Number,
    string Neighborhood,
    string City,
    string State,
    string? Complement,
    string? Reference);

public sealed record UpdateDefaultAddressRequest(string UserId, AddressDto Address);

// Auth & Orders (existing)
public sealed record CreateOrderRequest(string ClientId, string ServiceId, string? Description, string? Location, string? Date, int? MaxProposals = null);
public sealed record CompleteOrderRequest(string? ProfessionalId, string? ClientId);
public sealed record LoginRequest(string Email, string Senha);

// Social login
public sealed record GoogleLoginRequest(string IdToken);
public sealed record FacebookLoginRequest(string AccessToken);

// Confirmação de conta e recuperação de senha
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record ResendVerificationRequest(string Email);
public sealed record UpdateAppointmentStatusRequest(string Status);
public sealed record CreateAppointmentRequest(string ProfessionalId, string? ClientId, string? ServiceId, DateTime StartsAt, DateTime EndsAt, string? Location, string? Notes);

// Users
public sealed record CreateUserRequest(string Name, string Email, string? Phone, string Role, string Senha, string? ZoneId, AddressDto? DefaultAddress = null);
public sealed record UpdateUserRequest(string? Name, string? Phone, string? ZoneId, AddressDto? DefaultAddress);

// Professionals
public sealed record CreateProfessionalRequest(string UserId, string? Bio, string[]? Zones, bool? Active);
public sealed record UpdateProfessionalRequest(string? Bio, bool? Active, string? AvailabilityText, string? AvatarUrl);

// Professional Services
public sealed record CreateProfessionalServiceRequest(
    string ProfessionalId,
    string ServiceId,
    string NomeServico,
    decimal? Preco,
    string? Descricao,
    int? TierId = null,
    string? ContractMode = null,
    int? DurationMinutes = null,
    int? MinLeadTimeMinutes = null,
    string? TipoContratacao = null);
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
    string? PaymentMethod,
    string? Scope,
    string? ScheduledAt,
    string? ConversationId,
    bool UseDefaultAddress = false,
    AddressDto? ServiceAddress = null,
    string? Description = null);

// Order from accepted proposal (Tier 2/3)
public sealed record CreateFromProposalRequest(
    string ClientId,
    string? PaymentMethod,
    bool UseDefaultAddress = false,
    AddressDto? ServiceAddress = null);

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
    int VisitFeeCents,
    string? SourceOrderId = null);

public sealed record SendProposalRequest(string ProfessionalId);
public sealed record AcceptProposalRequest(
    string ClientId,
    string? PaymentMethod,
    bool UseDefaultAddress = false,
    AddressDto? ServiceAddress = null);
public sealed record RejectProposalRequest(string ClientId, string? Reason);
public sealed record NegotiateProposalRequest(string ActorId, string ActorRole, string? CounterScope, int? CounterPriceCents);

// ─── Phase 3: Disputes ───────────────────────────────────────────────────────

public sealed record OpenDisputeRequest(
    string OrderId,
    string ClientId,
    string Reason,
    string? Description,
    string[]? EvidenceUrls);    // photo/file URLs already uploaded to storage

public sealed record RespondDisputeRequest(
    string ProfessionalId,
    string Response,
    string[]? EvidenceUrls);

public sealed record ResolveDisputeRequest(
    string Resolution,
    string ResolvedBy,          // system | mediator | agreement
    int? RefundAmountCents);

// ─── Phase 3: Expanded Reviews ───────────────────────────────────────────────

public sealed record CreateExpandedReviewRequest(
    string ProfessionalId,
    string ClientId,
    string OrderId,
    int Rating,
    string? Comment,
    int? PunctualityRating,
    int? QualityRating,
    int? CommunicationRating,
    int? CleanlinessRating,
    string[]? PhotoUrls);

public sealed record ProfessionalReviewClientRequest(
    string ProfessionalId,
    string OrderId,
    string Review,
    int? Rating);

// ─── Phase 4: Rebook + Recorrência ───────────────────────────────────────────

/// <summary>
/// Request body for POST /orders/rebook/{orderId}.
/// Creates a new order copying the original's professional, service, and price.
/// Optionally enrolls the client in a recurring plan with a discount.
/// </summary>
public sealed record RebookOrderRequest(
    string ClientId,
    string? ScheduledAt,
    string? PaymentMethod,
    bool UseDefaultAddress = false,
    AddressDto? ServiceAddress = null,
    /// <summary>If true, a recurring plan is created automatically.</summary>
    bool CreateRecurringPlan = false,
    /// <summary>Billing frequency when CreateRecurringPlan = true. Default: monthly.</summary>
    string Frequency = "monthly",
    /// <summary>Recurring discount percentage (0–100). e.g. 10 = 10% off each recurrence.</summary>
    int DiscountPercent = 0);

/// <summary>Request body for PATCH /recurring-plans/{id}/pause.</summary>
public sealed record PauseRecurringPlanRequest(string ClientId);

/// <summary>Request body for PATCH /recurring-plans/{id}/resume.</summary>
public sealed record ResumeRecurringPlanRequest(string ClientId);

/// <summary>Request body for DELETE /recurring-plans/{id}.</summary>
public sealed record CancelRecurringPlanRequest(string ClientId);
