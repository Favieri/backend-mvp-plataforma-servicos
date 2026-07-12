namespace Domain.Entities;

public class Proposal
{
    public string Id { get; private set; } = default!;
    public string? OrderId { get; private set; }
    /// <summary>Pedido-lead de origem (aberto sem profissional) que deu origem a esta proposta, se houver.</summary>
    public string? SourceOrderId { get; private set; }
    public string ProfessionalId { get; private set; } = default!;
    public string ClientId { get; private set; } = default!;
    public string ServiceId { get; private set; } = default!;
    public string? ProfessionalServiceId { get; private set; }
    public string? ConversationId { get; private set; }
    public string Scope { get; private set; } = default!;
    public string? IncludesDescription { get; private set; }
    public string? ExcludesDescription { get; private set; }
    public int PriceTotalCents { get; private set; }
    public string? PriceByStage { get; private set; }
    public string? DurationEstimate { get; private set; }
    public DateTime? SuggestedDatetime { get; private set; }
    public int VisitFeeCents { get; private set; }
    public DateTime ValidUntil { get; private set; }
    public string Status { get; private set; } = default!;
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Proposal() { }

    public static Proposal Create(
        string id,
        string professionalId,
        string clientId,
        string serviceId,
        string scope,
        int priceTotalCents,
        DateTime validUntil,
        string? professionalServiceId = null,
        string? conversationId = null,
        string? includesDescription = null,
        string? excludesDescription = null,
        string? priceByStage = null,
        string? durationEstimate = null,
        DateTime? suggestedDatetime = null,
        int visitFeeCents = 0,
        string? sourceOrderId = null)
    {
        var now = DateTime.UtcNow;
        return new Proposal
        {
            Id = id,
            SourceOrderId = sourceOrderId,
            ProfessionalId = professionalId,
            ClientId = clientId,
            ServiceId = serviceId,
            ProfessionalServiceId = professionalServiceId,
            ConversationId = conversationId,
            Scope = scope,
            IncludesDescription = includesDescription,
            ExcludesDescription = excludesDescription,
            PriceTotalCents = priceTotalCents,
            PriceByStage = priceByStage,
            DurationEstimate = durationEstimate,
            SuggestedDatetime = suggestedDatetime,
            VisitFeeCents = visitFeeCents,
            ValidUntil = validUntil,
            Status = Enums.ProposalStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Send()
    {
        Status = Enums.ProposalStatus.Sent;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Accept(string orderId)
    {
        Status = Enums.ProposalStatus.Accepted;
        OrderId = orderId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reject(string? reason = null)
    {
        Status = Enums.ProposalStatus.Rejected;
        RejectionReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    public void StartNegotiation()
    {
        Status = Enums.ProposalStatus.Negotiating;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Expire()
    {
        Status = Enums.ProposalStatus.Expired;
        UpdatedAt = DateTime.UtcNow;
    }
}
