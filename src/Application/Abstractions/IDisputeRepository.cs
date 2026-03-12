using Domain.Entities;

namespace Application.Abstractions;

public interface IDisputeRepository
{
    Task<Dispute?> GetByIdAsync(string id, CancellationToken ct);
    Task<Dispute?> GetByOrderIdAsync(string orderId, CancellationToken ct);
    Task<IReadOnlyList<object>> GetByProfessionalAsync(string professionalId, CancellationToken ct);
    Task<IReadOnlyList<object>> GetByClientAsync(string clientId, CancellationToken ct);
    Task<Dispute> CreateAsync(Dispute dispute, CancellationToken ct);
    Task<bool> AddProfessionalResponseAsync(string id, string response, string? evidenceUrls, CancellationToken ct);
    Task<bool> ResolveAsync(string id, string resolution, string resolvedBy, int? refundAmountCents, CancellationToken ct);
    Task<bool> EscalateAsync(string id, CancellationToken ct);
    Task<bool> CloseAsync(string id, CancellationToken ct);
    Task<bool> OrderHasOpenDisputeAsync(string orderId, CancellationToken ct);
}
