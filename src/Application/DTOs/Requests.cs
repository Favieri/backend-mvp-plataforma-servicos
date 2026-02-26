namespace Application.DTOs;

public sealed record CreateOrderRequest(string ClientId, string ServiceId, string? Description, string? Location, string? Date);
public sealed record CompleteOrderRequest(string? ProfessionalId, string? ClientId);
public sealed record LoginRequest(string Email, string Senha);
public sealed record CreatePaymentPreferenceRequest(string OrderId, int AmountCents, string? Method, string? Title);
public sealed record MercadoPagoWebhookRequest(string? Id, string? Type, string? Action, Dictionary<string, object>? Data);
public sealed record UpdateAppointmentStatusRequest(string Status);
public sealed record CreateAppointmentRequest(string ProfessionalId, string? ClientId, string? ServiceId, DateTime StartsAt, DateTime EndsAt, string? Location, string? Notes);
