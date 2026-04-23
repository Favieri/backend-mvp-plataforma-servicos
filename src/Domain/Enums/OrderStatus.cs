namespace Domain.Enums;

public static class OrderStatus
{
    // ─── Legacy statuses (backward-compatible) ──────────────────────────────
    public const string Aberto = "aberto";
    public const string Confirmado = "confirmado";
    public const string Concluido = "concluido";
    public const string AutoConcluido = "auto_concluido";
    public const string Cancelado = "cancelado";

    // ─── Phase 1: full transactional state machine ───────────────────────────
    public const string Draft = "draft";
    public const string ProposalSent = "proposal_sent";
    public const string AwaitingPayment = "awaiting_payment";
    public const string Scheduled = "scheduled";
    public const string InTransit = "in_transit";
    public const string InProgress = "in_progress";
    public const string AwaitingConfirmation = "awaiting_confirmation";
    public const string Completed = "completed";
    public const string Evaluated = "evaluated";
    public const string Disputed = "disputed";
    public const string CancelledClient = "cancelled_client";
    public const string CancelledProfessional = "cancelled_professional";
    public const string Refunded = "refunded";
    public const string Rebooked = "rebooked";
    public const string PaymentExpired = "payment_expired";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Aberto, Confirmado, Concluido, AutoConcluido, Cancelado,
        Draft, ProposalSent, AwaitingPayment, Scheduled, InTransit,
        InProgress, AwaitingConfirmation, Completed, Evaluated,
        Disputed, CancelledClient, CancelledProfessional, Refunded, Rebooked,
        PaymentExpired
    };

    public static readonly IReadOnlySet<string> Terminal = new HashSet<string>
    {
        Concluido, AutoConcluido, Cancelado, Completed, Evaluated,
        CancelledClient, CancelledProfessional, Refunded, Rebooked
    };
}
