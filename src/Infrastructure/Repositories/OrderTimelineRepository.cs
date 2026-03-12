using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class OrderTimelineRepository(AppDbContext ctx) : IOrderTimelineRepository
{
    public async Task AddEventAsync(OrderTimeline timeline, CancellationToken ct)
    {
        ctx.OrderTimelines.Add(timeline);
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OrderTimeline>> GetByOrderIdAsync(string orderId, CancellationToken ct)
        => await ctx.OrderTimelines
            .AsNoTracking()
            .Where(t => t.OrderId == orderId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
}
