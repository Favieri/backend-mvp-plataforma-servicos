using System.Text.RegularExpressions;
using Application.Abstractions;

namespace Application.Services;

/// <summary>
/// Serviço de detecção anti-fuga: identifica padrões de contato externo em mensagens de chat
/// (telefones, e-mails e URLs) para evitar que clientes e profissionais troquem contatos
/// fora da plataforma antes de um pedido pago ser confirmado.
/// A detecção não bloqueia o envio — apenas insere uma mensagem system de aviso.
/// </summary>
public sealed class AntiLeakDetectionService : IAntiLeakDetectionService
{
    // Telefone: +55, 0800, formatos com 8-11 dígitos com separadores opcionais
    private static readonly Regex PhoneRegex = new(
        @"(?:\+?55\s?)?(?:\(?\d{2}\)?[\s\-]?)?\d{4,5}[\s\-]?\d{4}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // E-mail básico
    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // URLs externas (http/https/www)
    private static readonly Regex UrlRegex = new(
        @"(?:https?://|www\.)\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // WhatsApp / Telegram / Instagram handles
    private static readonly Regex SocialHandleRegex = new(
        @"(?:whatsapp|whats|zap|telegram|t\.me|instagram|insta|@\w{3,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool HasLeakPattern(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        return PhoneRegex.IsMatch(text)
            || EmailRegex.IsMatch(text)
            || UrlRegex.IsMatch(text)
            || SocialHandleRegex.IsMatch(text);
    }

    public string GetWarningText() =>
        "⚠️ Parece que você tentou compartilhar dados de contato. " +
        "Para sua proteção, todas as negociações e pagamentos devem ser realizados " +
        "dentro da plataforma. Isso garante seu acesso à proteção ao cliente e ao suporte em caso de disputas.";
}
