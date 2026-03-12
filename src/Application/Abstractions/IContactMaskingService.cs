namespace Application.Abstractions;

public interface IContactMaskingService
{
    /// <summary>
    /// Determina se os campos de contato (telefone, e-mail) devem ser mascarados
    /// para uma conversa. O mascaramento é aplicado enquanto não houver pedido pago
    /// vinculado à conversa.
    /// </summary>
    bool ShouldMask(string? orderStatus);

    /// <summary>
    /// Mascara um valor de e-mail (ex: j***@example.com).
    /// </summary>
    string? MaskEmail(string? email);

    /// <summary>
    /// Mascara um número de telefone (ex: +5511*****9999).
    /// </summary>
    string? MaskPhone(string? phone);
}
