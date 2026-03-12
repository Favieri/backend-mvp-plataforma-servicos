using Application.Abstractions;
using Domain.Enums;

namespace Application.Services;

/// <summary>
/// Serviço de mascaramento de contato: oculta e-mail e telefone de clientes e profissionais
/// enquanto não houver pedido pago vinculado à conversa.
/// O mascaramento é retirado quando o pedido atingir um status igual ou posterior a "scheduled".
/// </summary>
public sealed class ContactMaskingService : IContactMaskingService
{
    // Statuses nos quais o contato pode ser revelado (pedido confirmado/pago)
    private static readonly HashSet<string> UnmaskedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        OrderStatus.Scheduled,
        OrderStatus.InProgress,
        OrderStatus.AwaitingConfirmation,
        OrderStatus.Completed,
        OrderStatus.Disputed,
    };

    public bool ShouldMask(string? orderStatus)
    {
        if (string.IsNullOrWhiteSpace(orderStatus)) return true;
        return !UnmaskedStatuses.Contains(orderStatus);
    }

    public string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return email;

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return "***@***";

        var local = email[..atIndex];
        var domain = email[atIndex..];

        var visibleChars = Math.Min(1, local.Length);
        var maskedLocal = local[..visibleChars] + new string('*', Math.Max(0, local.Length - visibleChars));

        return $"{maskedLocal}{domain}";
    }

    public string? MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return phone;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 4) return "****";

        // Mantém os 2 últimos dígitos visíveis
        var visible = digits[^2..];
        var masked = new string('*', digits.Length - 2) + visible;

        return masked;
    }
}
