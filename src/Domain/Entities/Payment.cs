namespace Domain.Entities;

public class Payment
{
    public string Id { get; private set; } = default!;
    public string OrderId { get; private set; } = default!;
    public string Gateway { get; private set; } = default!;
    public string? GatewayRef { get; private set; }
    public string Method { get; private set; } = "unknown";
    public int AmountCents { get; private set; }
    public int PlatformFeeCents { get; private set; }
    public int GatewayFeeCents { get; private set; }
    public string Status { get; private set; } = "pending";
    public DateTime CreatedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }
    // Populated by PRD-MP-04 webhook handler
    public string? MpPaymentId { get; private set; }
    public string? PixCode { get; private set; }
    public string? PixQrCodeBase64 { get; private set; }
    public DateTime? PixExpiresAt { get; private set; }
    // Refund tracking (PRD-MP-09)
    public string? RefundStatus { get; private set; }   // null | pending | completed | failed
    public string? RefundId { get; private set; }
    public DateTime? RefundedAt { get; private set; }
    public string? RefundReason { get; private set; }

    private Payment() { }

    public static Payment CreateForMercadoPago(
        string id,
        string orderId,
        string? gatewayRef,
        int amountCents,
        int platformFeeCents) => new()
    {
        Id = id,
        OrderId = orderId,
        Gateway = "mercadopago",
        GatewayRef = gatewayRef,
        Method = "unknown",
        AmountCents = amountCents,
        PlatformFeeCents = platformFeeCents,
        Status = "pending",
        CreatedAt = DateTime.UtcNow
    };

    public void SetStatus(string status) => Status = status;
    public void SetGatewayRef(string gatewayRef) => GatewayRef = gatewayRef;
    public void SetMethod(string method) => Method = method;

    public void SetPaid(DateTime paidAt, int gatewayFeeCents)
    {
        PaidAt = paidAt;
        GatewayFeeCents = gatewayFeeCents;
    }

    public void SetMpPaymentId(string mpPaymentId) => MpPaymentId = mpPaymentId;

    public void SetPixDetails(string? code, string? qrCode, DateTime? expiresAt)
    {
        PixCode = code;
        PixQrCodeBase64 = qrCode;
        PixExpiresAt = expiresAt;
    }

    public void SetRefundPending(string reason)
    {
        RefundStatus = "pending";
        RefundReason = reason;
    }

    public void SetRefunded(string refundId, DateTime refundedAt, string reason)
    {
        RefundStatus = "completed";
        RefundId = refundId;
        RefundedAt = refundedAt;
        RefundReason = reason;
        Status = "refunded";
    }

    public void SetRefundFailed() => RefundStatus = "failed";
}
