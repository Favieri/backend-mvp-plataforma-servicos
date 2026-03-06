namespace Application.Abstractions;

public interface IPortfolioRepository
{
    Task<IReadOnlyList<object>> GetByProfessionalAsync(string professionalId, CancellationToken ct);
    Task<object?> GetByIdAsync(string id, CancellationToken ct);
    Task<object> CreateAsync(string professionalId, string imageUrl, string? title, string? description, CancellationToken ct);
    Task<object?> UpdateAsync(string id, string? title, string? description, string? imageUrl, int? orderIndex, CancellationToken ct);
    Task<bool> DeleteAsync(string id, CancellationToken ct);
}
