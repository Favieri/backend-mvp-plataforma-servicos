namespace Application.Abstractions;

public interface IMercadoPagoClient
{
    Task<(string PreferenceId, string InitPoint)> CreatePreferenceAsync(string orderId, int amountCents, string title, CancellationToken ct);
    Task<string> GetPaymentStatusAsync(string paymentId, CancellationToken ct);
}
