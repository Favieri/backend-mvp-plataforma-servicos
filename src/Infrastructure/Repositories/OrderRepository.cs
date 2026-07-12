using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class OrderRepository(AppDbContext ctx) : IOrderRepository
{
    // ─── Legacy methods (backward compatible) ────────────────────────────────

    public async Task<PagedResult<Order>> GetOrdersAsync(
        string? serviceId,
        string? excludeProfessionalId,
        string? professionalId,
        bool filterZones,
        bool active,
        int page,
        int pageSize,
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
            var activeTotal = await query.CountAsync(ct);
            var activeItems = await query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return new PagedResult<Order>(activeItems, activeTotal);
        }

        query = query.Where(o => o.ProfessionalId == null && o.Status == OrderStatus.Aberto);

        if (!string.IsNullOrWhiteSpace(serviceId))
            query = query.Where(o => o.ServiceId == serviceId);

        if (!string.IsNullOrWhiteSpace(excludeProfessionalId))
        {
            query = query.Where(o =>
                !ctx.ProfessionalOrderIgnores
                    .Any(poi => poi.ProfessionalId == excludeProfessionalId && poi.OrderId == o.Id));
        }

        if (filterZones && !string.IsNullOrWhiteSpace(professionalId))
        {
            var professionalZones = await ctx.ProfessionalZones
                .Where(pz => pz.ProfessionalId == professionalId)
                .Select(pz => pz.ZoneId)
                .ToListAsync(ct);

            var candidates = await query
                .Select(o => new
                {
                    Order = o,
                    ClientZoneId = ctx.Users.Where(u => u.Id == o.ClientId).Select(u => u.ZoneId).FirstOrDefault()
                })
                .Where(x => x.ClientZoneId != null && professionalZones.Contains(x.ClientZoneId))
                .ToListAsync(ct);

            // Janela de visibilidade por reputação: começa pequena (melhor avaliados) e cresce com o
            // tempo decorrido desde a criação do pedido, até cobrir todos os profissionais compatíveis.
            var visible = new List<Order>();
            var zoneRankCache = new Dictionary<string, List<string>>();
            foreach (var c in candidates)
            {
                var zoneId = c.ClientZoneId!;
                if (!zoneRankCache.TryGetValue(zoneId, out var ranked))
                {
                    ranked = await GetRankedProfessionalIdsForZoneAsync(zoneId, ct);
                    zoneRankCache[zoneId] = ranked;
                }

                var baseWindow = Math.Max(c.Order.MaxProposals * 3, 5);
                var elapsedHours = (DateTime.UtcNow - c.Order.CreatedAt).TotalHours;
                var expansionFactor = 1 + Math.Floor(elapsedHours / 4) * 0.5;
                var effectiveWindow = Math.Min(ranked.Count, (int)(baseWindow * expansionFactor));

                if (ranked.Take(effectiveWindow).Contains(professionalId))
                    visible.Add(c.Order);
            }

            var visibleOrdered = visible.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).ToList();
            var pagedVisible = visibleOrdered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return new PagedResult<Order>(pagedVisible, visibleOrdered.Count);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<Order>(items, totalCount);
    }

    public async Task<Order?> GetByIdAsync(string id, CancellationToken ct)
        => await ctx.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<Order> CreateAsync(
        string clientId, string serviceId, string? description, string? location,
        DateTime? date, CancellationToken ct, int? maxProposals = null)
    {
        var order = Order.Create(
            id: Guid.NewGuid().ToString(),
            clientId: clientId,
            serviceId: serviceId,
            description: description,
            location: location,
            date: date,
            maxProposals: maxProposals);

        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync(ct);
        return order;
    }

    public async Task CompleteOrderAsync(string orderId, CancellationToken ct)
        => await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Concluido), ct);

    public async Task<PagedResult<object>> GetMineAsync(string clientId, int page, int pageSize, CancellationToken ct)
    {
        // Inclui service.name, professional.name, location, totalCents e serviceAddress para que o
        // front-end possa exibir os dados completos na listagem de pedidos.
        var baseQuery = ctx.Orders
            .AsNoTracking()
            .Where(o => o.ClientId == clientId);

        var totalCount = await baseQuery.CountAsync(ct);

        var rows = await baseQuery
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                id = o.Id,
                status = o.Status,
                scheduledAt = o.ScheduledAt ?? o.Date,
                createdAt = o.CreatedAt,
                location = o.Location,
                totalCents = o.PriceTotalCents,
                serviceAddress = o.SvcAddrZipCode != null ? new
                {
                    zipCode = o.SvcAddrZipCode,
                    street = o.SvcAddrStreet,
                    number = o.SvcAddrNumber,
                    neighborhood = o.SvcAddrNeighborhood,
                    city = o.SvcAddrCity,
                    state = o.SvcAddrState,
                    complement = o.SvcAddrComplement,
                    reference = o.SvcAddrReference
                } : null,
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

        return new PagedResult<object>(rows.Cast<object>().ToList(), totalCount);
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
        // When role=professional, userId can be either:
        //   - User.Id (JWT sub claim) — resolved via Professional.UserId
        //   - Professional.Id (used by some frontend flows)
        // We support both by checking (pr.UserId == userId OR pr.Id == userId).
        var query = role == "professional"
            ? ctx.Orders.AsNoTracking().Where(o =>
                ctx.Professionals.Any(pr => (pr.UserId == userId || pr.Id == userId) && pr.Id == o.ProfessionalId))
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
                createdAt = o.CreatedAt,
                serviceAddress = o.SvcAddrZipCode != null ? new
                {
                    zipCode = o.SvcAddrZipCode,
                    street = o.SvcAddrStreet,
                    number = o.SvcAddrNumber,
                    neighborhood = o.SvcAddrNeighborhood,
                    city = o.SvcAddrCity,
                    state = o.SvcAddrState,
                    complement = o.SvcAddrComplement,
                    reference = o.SvcAddrReference
                } : null
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

    public async Task<bool> MarkRefundedAsync(string orderId, CancellationToken ct)
    {
        var rows = await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Refunded), ct);
        return rows > 0;
    }

    // ─── Lead flow: limite do cliente + priorização por reputação ────────────

    public async Task<bool> MarkConvertidoAsync(string orderId, CancellationToken ct)
    {
        var rows = await ctx.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Convertido), ct);
        return rows > 0;
    }

    public async Task<bool> MarkPropostasCompletasAsync(string orderId, CancellationToken ct)
    {
        var rows = await ctx.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.Aberto)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.PropostasCompletas), ct);
        return rows > 0;
    }

    public async Task<bool> ReopenAbertoAsync(string orderId, CancellationToken ct)
    {
        var rows = await ctx.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.PropostasCompletas)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Aberto), ct);
        return rows > 0;
    }

    /// <summary>
    /// Ranking de profissionais compatíveis com uma zona, por score de reputação (melhor primeiro).
    /// Reaproveita os campos já calculados por TrustMetricsService (Rating, CompletionRate, ResponseRate),
    /// com valores neutros de fallback para quem ainda não tem histórico suficiente.
    /// </summary>
    private async Task<List<string>> GetRankedProfessionalIdsForZoneAsync(string zoneId, CancellationToken ct)
    {
        var candidates = await ctx.ProfessionalZones
            .Where(pz => pz.ZoneId == zoneId)
            .Join(ctx.Professionals, pz => pz.ProfessionalId, p => p.Id, (pz, p) => p)
            .Where(p => p.Active)
            .Select(p => new { p.Id, p.Rating, p.CompletionRate, p.ResponseRate })
            .ToListAsync(ct);

        return candidates
            .Select(p => new
            {
                p.Id,
                Score = (p.Rating ?? 3.0) * 0.5 + (p.CompletionRate ?? 0.5) * 0.3 + (p.ResponseRate ?? 0.5) * 0.2
            })
            .OrderByDescending(x => x.Score)
            .Select(x => x.Id)
            .ToList();
    }
}
