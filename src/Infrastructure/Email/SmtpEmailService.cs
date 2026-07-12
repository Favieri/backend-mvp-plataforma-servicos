using System.Net;
using System.Net.Mail;
using Application.Abstractions;
using Dapper;
using Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Email;

/// <summary>
/// TODO: CREDENTIALS - configure SMTP credentials via env vars:
///   SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS, EMAIL_FROM
/// If SMTP_HOST is not configured, emails are logged but not sent (dry-run mode).
/// </summary>
public sealed class SmtpEmailService(IConnectionFactory factory, ILogger<SmtpEmailService> logger) : IEmailService
{
    private static readonly string DefaultFrom =
        Environment.GetEnvironmentVariable("EMAIL_FROM") ?? "Jobeasy <naoresponda@jobeasy.com.br>";

    private static readonly string AppBaseUrl =
        Environment.GetEnvironmentVariable("APP_BASE_URL") ?? "https://jobeasy.com.br";

    private static readonly bool EmailEnabled =
        Environment.GetEnvironmentVariable("EMAIL_ENABLED") is null or "true";

    public async Task SendAsync(string to, string subject, string html, string? text = null, string? dedupeKey = null, CancellationToken ct = default)
    {
        if (!EmailEnabled)
        {
            logger.LogInformation("[EMAIL:DISABLED] To={To} Subject={Subject}", to, subject);
            return;
        }

        // Deduplication via EmailJob table
        if (!string.IsNullOrWhiteSpace(dedupeKey))
        {
            var inserted = await TryInsertEmailJobAsync(to, subject, html, text, dedupeKey, ct);
            if (!inserted)
            {
                logger.LogDebug("[EMAIL:DEDUPE] Skipped duplicate email To={To} DedupeKey={Key}", to, dedupeKey);
                return;
            }
        }

        await DeliverAsync(to, subject, html, ct);
    }

    public async Task SendNewLeadAsync(string to, string professionalName, string clientName, string serviceName, string leadUrl, string? city = null, CancellationToken ct = default)
    {
        var (subject, html, _) = EmailTemplates.NewLeadProfessional(professionalName, clientName, serviceName, leadUrl, city);
        var dedupeKey = $"lead.new|{to}|{clientName}|{serviceName}";
        await SendAsync(to, subject, html, dedupeKey: dedupeKey, ct: ct);
    }

    public async Task SendChatMessageAsync(string to, string recipientName, string senderName, string messageSnippet, string chatUrl, string conversationId, int windowMinutes = 10, CancellationToken ct = default)
    {
        var (subject, html, _) = EmailTemplates.ChatNewMessage(recipientName, senderName, messageSnippet, chatUrl);
        var bucket = (long)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / (windowMinutes * 60_000L));
        var dedupeKey = $"chat.message|{conversationId}|{to}|{bucket}";
        await SendAsync(to, subject, html, dedupeKey: dedupeKey, ct: ct);
    }

    public async Task SendBookingConfirmedProfessionalAsync(string to, string professionalName, string clientName, string serviceName, string when, string bookingUrl, string? address = null, string? dedupeKey = null, CancellationToken ct = default)
    {
        var (subject, html, _) = EmailTemplates.BookingConfirmedProfessional(professionalName, clientName, serviceName, when, bookingUrl, address);
        await SendAsync(to, subject, html, dedupeKey: dedupeKey, ct: ct);
    }

    public async Task SendBookingConfirmedClientAsync(string to, string clientName, string professionalName, string serviceName, string when, string bookingUrl, string? address = null, string? dedupeKey = null, CancellationToken ct = default)
    {
        var (subject, html, _) = EmailTemplates.BookingConfirmedClient(clientName, professionalName, serviceName, when, bookingUrl, address);
        await SendAsync(to, subject, html, dedupeKey: dedupeKey, ct: ct);
    }

    public async Task SendEmailVerificationAsync(string to, string name, string verificationUrl, CancellationToken ct = default)
    {
        var (subject, html, _) = EmailTemplates.EmailVerification(name, verificationUrl);
        await SendAsync(to, subject, html, ct: ct);
    }

    public async Task SendPasswordResetAsync(string to, string name, string resetUrl, CancellationToken ct = default)
    {
        var (subject, html, _) = EmailTemplates.PasswordReset(name, resetUrl);
        await SendAsync(to, subject, html, ct: ct);
    }

    public async Task SendSocialAccountReminderAsync(string to, string name, string provider, CancellationToken ct = default)
    {
        var (subject, html, _) = EmailTemplates.SocialAccountReminder(name, provider);
        await SendAsync(to, subject, html, ct: ct);
    }

    private async Task<bool> TryInsertEmailJobAsync(string to, string subject, string html, string? text, string dedupeKey, CancellationToken ct)
    {
        try
        {
            using var conn = await factory.CreateOpenConnectionAsync(ct);
            var affected = await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into "EmailJob"(id,"to",subject,html,text,status,"dedupeKey","createdAt")
                values(gen_random_uuid()::text,@to,@subject,@html,@text,'pending',@dedupeKey,now())
                on conflict ("dedupeKey") do nothing
                """,
                new { to, subject, html, text, dedupeKey }, cancellationToken: ct));
            return affected > 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[EMAIL] Failed to check/insert dedupeKey={Key}, proceeding with send", dedupeKey);
            return true;
        }
    }

    private async Task DeliverAsync(string to, string subject, string html, CancellationToken ct)
    {
        var host = Environment.GetEnvironmentVariable("SMTP_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            // TODO: CREDENTIALS - set SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS to enable email delivery
            logger.LogInformation("[EMAIL:DRYRUN] No SMTP_HOST configured. To={To} Subject={Subject}", to, subject);
            return;
        }

        try
        {
            var port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var p) ? p : 587;
            var user = Environment.GetEnvironmentVariable("SMTP_USER") ?? "";
            var pass = Environment.GetEnvironmentVariable("SMTP_PASS") ?? "";

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = port is 587 or 465,
                Credentials = new NetworkCredential(user, pass)
            };

            using var msg = new MailMessage(DefaultFrom, to, subject, html) { IsBodyHtml = true };
            await client.SendMailAsync(msg, ct);
            logger.LogInformation("[EMAIL:SENT] To={To} Subject={Subject}", to, subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EMAIL:ERROR] Failed to send To={To} Subject={Subject}", to, subject);
        }
    }
}
