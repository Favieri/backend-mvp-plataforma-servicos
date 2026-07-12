using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Application.Abstractions;
using Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Email;

/// <summary>
/// Envia e-mails via Amazon SES. Autenticação exclusivamente via IAM role do Lambda
/// (cadeia de credenciais padrão do SDK) — nenhuma chave de acesso é configurada aqui,
/// mesmo padrão já usado para o cliente S3 em AvatarStorageRepository/AttachmentStorageRepository.
/// </summary>
public sealed class SesEmailService(
    IAmazonSimpleEmailServiceV2 sesClient,
    IConnectionFactory factory,
    ILogger<SesEmailService> logger) : IEmailService
{
    // Propriedades (não campos estáticos) para que testes possam alternar as env vars
    // entre casos sem depender de ordem de inicialização do tipo.
    private static string DefaultFrom =>
        Environment.GetEnvironmentVariable("EMAIL_FROM") ?? "Jobeasy <naoresponda@jobeasy.com.br>";

    private static bool EmailEnabled =>
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
            var inserted = await EmailDedupeGuard.TryInsertAsync(factory, to, subject, html, text, dedupeKey, logger, ct);
            if (!inserted)
            {
                logger.LogDebug("[EMAIL:DEDUPE] Skipped duplicate email To={To} DedupeKey={Key}", to, dedupeKey);
                return;
            }
        }

        await DeliverAsync(to, subject, html, text, ct);
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

    private async Task DeliverAsync(string to, string subject, string html, string? text, CancellationToken ct)
    {
        try
        {
            var request = new SendEmailRequest
            {
                FromEmailAddress = DefaultFrom,
                Destination = new Destination { ToAddresses = [to] },
                Content = new EmailContent
                {
                    Simple = new Message
                    {
                        Subject = new Content { Data = subject, Charset = "UTF-8" },
                        Body = new Body
                        {
                            Html = new Content { Data = html, Charset = "UTF-8" },
                            Text = text is not null ? new Content { Data = text, Charset = "UTF-8" } : null,
                        },
                    },
                },
            };

            var response = await sesClient.SendEmailAsync(request, ct);
            logger.LogInformation("[EMAIL:SENT] To={To} Subject={Subject} MessageId={MessageId}", to, subject, response.MessageId);
        }
        catch (Exception ex)
        {
            // Nunca deixar uma falha de envio de e-mail quebrar o fluxo que a chamou
            // (cadastro, recuperação de senha, etc.) — mesmo comportamento do SmtpEmailService.
            logger.LogError(ex, "[EMAIL:ERROR] Falha ao enviar via SES. To={To} Subject={Subject}", to, subject);
        }
    }
}
