namespace Application.Abstractions;

public interface IRefundService
{
    Task<RefundResult> RefundOrderAsync(
        string orderId,
        string reason,
        int? amountCents,
        CancellationToken ct);
}

public sealed record RefundResult(
    bool Success,
    string? RefundId,
    int AmountCents,
    string? ErrorCode,
    string? ErrorMessage
);
