namespace Application.Abstractions;

public interface IMercadoPagoService
{
    Task<MpPreferenceResult> CreatePreferenceAsync(
        CreatePreferenceRequest request,
        string professionalAccessToken,
        CancellationToken ct);

    Task<MpPaymentDetails?> GetPaymentDetailsAsync(string mpPaymentId, CancellationToken ct);

    Task<MpRefundResult> RefundPaymentAsync(string mpPaymentId, int? amountCents, CancellationToken ct);
}

public sealed record CreatePreferenceRequest(
    string OrderId,
    string ServiceName,
    int AmountCents,
    int PlatformFeeCents,
    int MaxInstallments,
    string? PayerEmail,
    string? PayerCpf,
    string BackUrlSuccess,
    string BackUrlFailure,
    string BackUrlPending,
    string NotificationUrl
);

public sealed record MpPreferenceResult(
    string PreferenceId,
    string CheckoutUrl,
    string SandboxUrl,
    DateTime ExpiresAt,
    bool IsSandbox = false
);

public sealed record MpRefundResult(
    bool Success,
    string? RefundId,
    string? ErrorCode
);

public sealed record MpPaymentDetails(
    string MpPaymentId,
    string Status,
    string? PaymentTypeId,
    int TransactionAmountCents,
    DateTime? DateApproved,
    int? TransactionNetAmountCents,
    string? StatusDetail = null,
    string? ExternalReference = null,
    int? MarketplaceFeeCents = null,
    int? MpGatewayFeeCents = null,
    string? PixCode = null,
    string? PixQrCodeBase64 = null,
    DateTime? PixExpiresAt = null
);
