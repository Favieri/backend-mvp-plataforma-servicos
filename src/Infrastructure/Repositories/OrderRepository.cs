using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class OrderRepository(AppDbContext ctx) : IOrderRepository
{
    // ─── Legacy methods (backward compatible) ────────────────────────────────

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(
        string? serviceId,
        string? excludeProfessionalId,
        string? professionalId,
        bool filterZones,
        bool active,
        CancellationToken ct)
    {
        var query = ctx.Orders.AsNoTracking();

        // active=true: retorna somente pedidos atribuídos a este profissional com status não-terminal.
        // Usado pela tela "Pedidos Ativos" da área do profissional.
        // Este bloco é mutuamente exclusivo com filterZones (leads) para evitar interferência.
        if (active && !string.IsNullOrWhiteSpace(professionalId) && !filterZones)
        {
            var terminalStatuses = OrderStatus.Terminal.ToList();
            query = query.Where(o => o.ProfessionalId == professionalId);
            query = query.Where(o => !terminalStatuses.Contains(o.Status));
            return await query.OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
        }

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
        var order = Order.Create(
            id: Guid.NewGuid().ToString(),
            clientId: clientId,
            serviceId: serviceId,
            description: description,
            location: location,
            date: date);

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
        // Inclui service.name, professional.name, location e totalCents para que o
        // front-end possa exibir os dados completos na listagem de pedidos.
        var rows = await ctx.Orders
            .AsNoTracking()
            .Where(o => o.ClientId == clientId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                id = o.Id,
                status = o.Status,
                scheduledAt = o.ScheduledAt ?? o.Date,
                createdAt = o.CreatedAt,
                location = o.Location,
                totalCents = o.PriceTotalCents,
                service = ctx.Services
                    .Where(s => s.Id == o.ServiceId)
                    .Select(s => new { id = s.Id, name = s.Name })
                    .FirstOrDefault(),
                professional = o.ProfessionalId != null
                    ? ctx.Professionals
                        .Where(p => p.Id == o.ProfessionalId)
                        .Select(p => new
                        {
                            id = p.Id,
                            avatarUrl = p.AvatarUrl,
                            name = ctx.Users.Where(u => u.Id == p.UserId).Select(u => u.Name).FirstOrDefault()
                        })
                        .FirstOrDefault()
                    : null
            })
            .ToListAsync(ct);

        return rows.Cast<object>().ToList();
    }

    // ─── Phase 1 methods ──────────────────────────────────────────────────────

    public async Task<Order> CreateBookingAsync(Order order, CancellationToken ct)
    {
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync(ct);
        return order;
    }

    public async Task<Order> CreateFromProposalAsync(Order order, CancellationToken ct)
    {
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync(ct);
        return order;
    }

    public async Task<bool> UpdateStatusAsync(string orderId, string newStatus, CancellationToken ct)
    {
        var rows = await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, newStatus), ct);
        return rows > 0;
    }

    public async Task<bool> MarkAwaitingConfirmationAsync(string orderId, int autoConfirmHours, CancellationToken ct)
    {
        var autoConfirmAt = DateTime.UtcNow.AddHours(autoConfirmHours);
        var rows = await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.AwaitingConfirmation)
                .SetProperty(o => o.AutoConfirmAt, autoConfirmAt), ct);
        return rows > 0;
    }

    public async Task<bool> MarkCompletedAsync(string orderId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var rows = await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Completed)
                .SetProperty(o => o.CompletedAt, now)
                .SetProperty(o => o.AutoConfirmAt, (DateTime?)null), ct);
        return rows > 0;
    }

    public async Task<bool> MarkCancelledByClientAsync(string orderId, string? reason, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var rows = await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.CancelledClient)
                .SetProperty(o => o.CancelledAt, now)
                .SetProperty(o => o.CancelledBy, ActorRole.Client)
                .SetProperty(o => o.CancellationReason, reason), ct);
        return rows > 0;
    }

    public async Task<bool> MarkCancelledByProfessionalAsync(string orderId, string? reason, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var rows = await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.CancelledProfessional)
                .SetProperty(o => o.CancelledAt, now)
                .SetProperty(o => o.CancelledBy, ActorRole.Professional)
                .SetProperty(o => o.CancellationReason, reason), ct);
        return rows > 0;
    }

    public async Task<bool> MarkDisputedAsync(string orderId, CancellationToken ct)
    {
        var rows = await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Disputed), ct);
        return rows > 0;
    }

    public async Task<IReadOnlyList<object>> GetMineByRoleAsync(string userId, string role, CancellationToken ct)
    {
        var query = role == "professional"
            ? ctx.Orders.AsNoTracking().Where(o => o.ProfessionalId == userId)
            : ctx.Orders.AsNoTracking().Where(o => o.ClientId == userId);

        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                id = o.Id,
                status = o.Status,
                origin = o.Origin,
                tierId = o.TierId,
                priceTotalCents = o.PriceTotalCents,
                scheduledAt = o.ScheduledAt ?? o.Date,
                createdAt = o.CreatedAt
            })
            .ToListAsync(ct);

        return rows.Cast<object>().ToList();
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAwaitingAutoConfirmAsync(DateTime before, CancellationToken ct)
        => await ctx.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.AwaitingConfirmation
                        && o.AutoConfirmAt.HasValue
                        && o.AutoConfirmAt.Value <= before)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> GetOrdersAwaitingPaymentTimedOutAsync(DateTime before, CancellationToken ct)
        => await ctx.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.AwaitingPayment
                        && o.CreatedAt <= before)
            .ToListAsync(ct);
}
