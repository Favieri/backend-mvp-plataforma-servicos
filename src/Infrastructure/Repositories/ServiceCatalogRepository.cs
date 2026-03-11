using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ServiceCatalogRepository(AppDbContext ctx) : IServiceCatalogRepository
{
    public async Task<IReadOnlyList<ServiceTier>> GetTiersAsync(CancellationToken ct)
        => await ctx.ServiceTiers.AsNoTracking().OrderBy(t => t.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<ServiceCategory>> GetCategoriesAsync(CancellationToken ct)
        => await ctx.ServiceCategories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
}
