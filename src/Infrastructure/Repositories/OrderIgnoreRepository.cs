using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class OrderIgnoreRepository(AppDbContext ctx) : IOrderIgnoreRepository
{
    public async Task UpsertAsync(string professionalId, string orderId, CancellationToken ct)
    {
        var exists = await ctx.ProfessionalOrderIgnores
            .AsNoTracking()
            .AnyAsync(x => x.ProfessionalId == professionalId && x.OrderId == orderId, ct);

        if (!exists)
        {
            ctx.ProfessionalOrderIgnores.Add(new ProfessionalOrderIgnore(professionalId, orderId, DateTime.UtcNow));
            await ctx.SaveChangesAsync(ct);
        }
    }

    public async Task DeleteAsync(string professionalId, string orderId, CancellationToken ct)
        => await ctx.ProfessionalOrderIgnores
            .Where(x => x.ProfessionalId == professionalId && x.OrderId == orderId)
            .ExecuteDeleteAsync(ct);

    public async Task<bool> ProfessionalExistsAsync(string professionalId, CancellationToken ct)
        => await ctx.Professionals.AsNoTracking().AnyAsync(p => p.Id == professionalId, ct);

    public async Task<bool> OrderExistsAsync(string orderId, CancellationToken ct)
        => await ctx.Orders.AsNoTracking().AnyAsync(o => o.Id == orderId, ct);
}
