using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class OrderRepository(AppDbContext ctx) : IOrderRepository
{
    public async Task<IReadOnlyList<Order>> GetOrdersAsync(
        string? serviceId,
        string? excludeProfessionalId,
        string? professionalId,
        bool filterZones,
        CancellationToken ct)
    {
        var query = ctx.Orders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(serviceId))
            query = query.Where(o => o.ServiceId == serviceId);

        if (filterZones && !string.IsNullOrWhiteSpace(professionalId))
        {
            var professionalZones = ctx.ProfessionalZones
                .Where(pz => pz.ProfessionalId == professionalId)
                .Select(pz => pz.ZoneId);

            query = query.Where(o =>
                ctx.Users
                    .Where(u => u.Id == o.ClientId && u.ZoneId != null && professionalZones.Contains(u.ZoneId!))
                    .Any());
        }

        if (!string.IsNullOrWhiteSpace(excludeProfessionalId))
        {
            query = query.Where(o =>
                !ctx.ProfessionalOrderIgnores
                    .Any(poi => poi.ProfessionalId == excludeProfessionalId && poi.OrderId == o.Id));
        }

        return await query.OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
    }

    public async Task<Order?> GetByIdAsync(string id, CancellationToken ct)
        => await ctx.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<Order> CreateAsync(
        string clientId, string serviceId, string? description, string? location,
        DateTime? date, CancellationToken ct)
    {
        var order = new Order(
            Id: Guid.NewGuid().ToString(),
            ClientId: clientId,
            ServiceId: serviceId,
            Description: description,
            Location: location,
            Date: date,
            Status: OrderStatus.Aberto,
            CreatedAt: DateTime.UtcNow);

        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync(ct);
        return order;
    }

    public async Task CompleteOrderAsync(string orderId, CancellationToken ct)
        => await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Concluido), ct);

    public async Task<IReadOnlyList<object>> GetMineAsync(string clientId, CancellationToken ct)
    {
        var rows = await ctx.Orders
            .AsNoTracking()
            .Where(o => o.ClientId == clientId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                id = o.Id,
                status = o.Status,
                scheduledAt = o.Date,
                createdAt = o.CreatedAt
            })
            .ToListAsync(ct);

        return rows.Cast<object>().ToList();
    }
}
