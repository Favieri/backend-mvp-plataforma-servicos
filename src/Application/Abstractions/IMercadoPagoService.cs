namespace Application.Abstractions;

public interface IMercadoPagoService
{
    Task<MpPreferenceResult> CreatePreferenceAsync(
        CreatePreferenceRequest request,
        string professionalAccessToken,
        CancellationToken ct);

    Task<MpPaymentDetails?> GetPaymentDetailsAsync(string mpPaymentId, CancellationToken ct);

    Task<bool> RefundPaymentAsync(string mpPaymentId, CancellationToken ct);
}

public sealed record CreatePreferenceRequest(
    string OrderId,
    string ServiceName,
    int AmountCents,
    int PlatformFeeCents,
    int MaxInstallments,
    string? PayerEmail,
    string BackUrlSuccess,
    string BackUrlFailure,
    string BackUrlPending,
    string NotificationUrl
);

public sealed record MpPreferenceResult(
    string PreferenceId,
    string CheckoutUrl,
    string SandboxUrl,
    DateTime ExpiresAt
);

public sealed record MpPaymentDetails(
    string MpPaymentId,
    string Status,
    string? PaymentTypeId,
    int TransactionAmountCents,
    DateTime? DateApproved,
    int? TransactionNetAmountCents
);
