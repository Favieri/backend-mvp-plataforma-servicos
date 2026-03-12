namespace Application.Abstractions;

public interface IPaymentOrchestrationService
{
    Task LinkToFinancialOrderAsync(string orderId, string financialOrderId, CancellationToken ct);
    Task<IReadOnlyList<string>> GetFinancialOrderIdsAsync(string orderId, CancellationToken ct);
    Task<object?> GetPaymentSummaryAsync(string orderId, CancellationToken ct);
}
