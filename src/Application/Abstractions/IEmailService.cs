namespace Application.Abstractions;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string html, string? text = null, string? dedupeKey = null, CancellationToken ct = default);

    // Template helpers
    Task SendNewLeadAsync(string to, string professionalName, string clientName, string serviceName, string leadUrl, string? city = null, CancellationToken ct = default);
    Task SendChatMessageAsync(string to, string recipientName, string senderName, string messageSnippet, string chatUrl, string conversationId, int windowMinutes = 10, CancellationToken ct = default);
    Task SendBookingConfirmedProfessionalAsync(string to, string professionalName, string clientName, string serviceName, string when, string bookingUrl, string? address = null, string? dedupeKey = null, CancellationToken ct = default);
    Task SendBookingConfirmedClientAsync(string to, string clientName, string professionalName, string serviceName, string when, string bookingUrl, string? address = null, string? dedupeKey = null, CancellationToken ct = default);
    Task SendEmailVerificationAsync(string to, string name, string verificationUrl, CancellationToken ct = default);
    Task SendPasswordResetAsync(string to, string name, string resetUrl, CancellationToken ct = default);
    Task SendSocialAccountReminderAsync(string to, string name, string provider, CancellationToken ct = default);
}
