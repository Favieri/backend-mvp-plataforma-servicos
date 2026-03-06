using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class PortfolioRepository(AppDbContext ctx) : IPortfolioRepository
{
    public async Task<IReadOnlyList<object>> GetByProfessionalAsync(string professionalId, CancellationToken ct)
    {
        var rows = await ctx.ProfessionalPortfolios
            .AsNoTracking()
            .Where(p => p.ProfessionalId == professionalId)
            .OrderBy(p => p.OrderIndex == null ? 1 : 0)
            .ThenBy(p => p.OrderIndex)
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                id = p.Id,
                professionalId = p.ProfessionalId,
                imageUrl = p.ImageUrl,
                title = p.Title,
                description = p.Description,
                orderIndex = p.OrderIndex,
                createdAt = p.CreatedAt
            })
            .ToListAsync(ct);

        return rows.Cast<object>().ToList();
    }

    public async Task<object?> GetByIdAsync(string id, CancellationToken ct)
        => await ctx.ProfessionalPortfolios
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                id = p.Id,
                professionalId = p.ProfessionalId,
                imageUrl = p.ImageUrl,
                title = p.Title,
                description = p.Description,
                orderIndex = p.OrderIndex,
                createdAt = p.CreatedAt
            })
            .FirstOrDefaultAsync(ct);

    public async Task<object> CreateAsync(
        string professionalId, string imageUrl, string? title, string? description, CancellationToken ct)
    {
        var maxIndex = await ctx.ProfessionalPortfolios
            .AsNoTracking()
            .Where(p => p.ProfessionalId == professionalId)
            .MaxAsync(p => (int?)p.OrderIndex, ct);

        var orderIndex = (maxIndex ?? -1) + 1;

        var portfolio = new ProfessionalPortfolio(
            Id: Guid.NewGuid().ToString(),
            ProfessionalId: professionalId,
            ImageUrl: imageUrl,
            Title: title,
            Description: description,
            OrderIndex: orderIndex,
            CreatedAt: DateTime.UtcNow);

        ctx.ProfessionalPortfolios.Add(portfolio);
        await ctx.SaveChangesAsync(ct);

        return new
        {
            id = portfolio.Id,
            professionalId = portfolio.ProfessionalId,
            imageUrl = portfolio.ImageUrl,
            title = portfolio.Title,
            description = portfolio.Description,
            orderIndex = portfolio.OrderIndex,
            createdAt = portfolio.CreatedAt
        };
    }

    public async Task<object?> UpdateAsync(
        string id, string? title, string? description, string? imageUrl, int? orderIndex, CancellationToken ct)
    {
        var existing = await ctx.ProfessionalPortfolios
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (existing is null) return null;

        // Merge patch: keep existing value when not provided
        var newTitle = title ?? existing.Title;
        var newDescription = description ?? existing.Description;
        var newImageUrl = imageUrl ?? existing.ImageUrl;
        var newOrderIndex = orderIndex ?? existing.OrderIndex;

        await ctx.ProfessionalPortfolios
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Title, newTitle)
                .SetProperty(p => p.Description, newDescription)
                .SetProperty(p => p.ImageUrl, newImageUrl)
                .SetProperty(p => p.OrderIndex, newOrderIndex), ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        var deleted = await ctx.ProfessionalPortfolios
            .Where(p => p.Id == id)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }
}
