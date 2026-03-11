using Domain.Entities;

namespace Application.Abstractions;

public interface IServiceCatalogRepository
{
    Task<IReadOnlyList<ServiceTier>> GetTiersAsync(CancellationToken ct);
    Task<IReadOnlyList<ServiceCategory>> GetCategoriesAsync(CancellationToken ct);
}
