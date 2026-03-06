using Domain.Entities;

namespace Application.Abstractions;

public interface IReviewRepository
{
    Task<object> GetByProfessionalAsync(string professionalId, int limit, CancellationToken ct);
    Task<object?> GetByIdAsync(string id, CancellationToken ct);
    Task<object> CreateAsync(string professionalId, string clientId, string orderId, int rating, string? comment, CancellationToken ct);
    Task<object?> UpdateAsync(string id, int? rating, string? comment, CancellationToken ct);
    Task<bool> OrderAlreadyReviewedAsync(string orderId, CancellationToken ct);
    Task<bool> OrderBelongsToClientAsync(string orderId, string clientId, CancellationToken ct);
    Task<string?> GetProfessionalUserIdAsync(string professionalId, CancellationToken ct);
    Task<IReadOnlyList<object>> GetEligibleOrdersAsync(string clientId, string professionalId, CancellationToken ct);
}
