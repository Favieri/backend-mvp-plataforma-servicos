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

    // ─── Phase 3: expanded review creation (categories + photos) ────────────
    Task<object> CreateExpandedAsync(
        string professionalId,
        string clientId,
        string orderId,
        int rating,
        string? comment,
        int? punctualityRating,
        int? qualityRating,
        int? communicationRating,
        int? cleanlinessRating,
        string? photoUrls,
        bool isVerified,
        CancellationToken ct);

    // ─── Phase 3: professional reviews client ────────────────────────────────
    Task<bool> OrderBelongsToProfessionalAsync(string orderId, string professionalId, CancellationToken ct);
    Task<bool> ProfessionalAlreadyReviewedClientAsync(string orderId, CancellationToken ct);
    Task<object> AddProfessionalReviewOfClientAsync(
        string orderId,
        string professionalId,
        string review,
        int? rating,
        CancellationToken ct);

    // ─── Phase 3: double-blind visibility unlock ─────────────────────────────
    Task UnlockDoubleBlindIfReadyAsync(string orderId, CancellationToken ct);
}
