namespace Domain.Entities;

public class Dispute
{
    public string Id { get; private set; } = default!;
    public string OrderId { get; private set; } = default!;
    public string ClientId { get; private set; } = default!;
    public string ProfessionalId { get; private set; } = default!;
    public string Reason { get; private set; } = default!;
    public string? Description { get; private set; }
    public string? EvidenceUrls { get; private set; }           // JSONB stored as string
    public string? ProfessionalResponse { get; private set; }
    public string? ProfessionalEvidenceUrls { get; private set; } // JSONB stored as string
    public string? Resolution { get; private set; }
    public string? ResolvedBy { get; private set; }              // system | mediator | agreement
    public int? RefundAmountCents { get; private set; }
    public string Status { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    private Dispute() { }

    public static Dispute Open(
        string id,
        string orderId,
        string clientId,
        string professionalId,
        string reason,
        string? description = null,
        string? evidenceUrls = null)
    {
        return new Dispute
        {
            Id = id,
            OrderId = orderId,
            ClientId = clientId,
            ProfessionalId = professionalId,
            Reason = reason,
            Description = description,
            EvidenceUrls = evidenceUrls,
            Status = Enums.DisputeStatus.Opened,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void AddProfessionalResponse(string response, string? evidenceUrls = null)
    {
        ProfessionalResponse = response;
        ProfessionalEvidenceUrls = evidenceUrls;
        Status = Enums.DisputeStatus.ProfessionalResponded;
    }

    public void EscalateToMediation() => Status = Enums.DisputeStatus.Mediating;

    public void Resolve(string resolution, string resolvedBy, int? refundAmountCents = null)
    {
        Status = Enums.DisputeStatus.Resolved;
        Resolution = resolution;
        ResolvedBy = resolvedBy;
        RefundAmountCents = refundAmountCents;
        ResolvedAt = DateTime.UtcNow;
    }

    public void Close()
    {
        Status = Enums.DisputeStatus.Closed;
        ResolvedAt ??= DateTime.UtcNow;
    }
}
