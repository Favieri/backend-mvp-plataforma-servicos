using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class LedgerRepository(AppDbContext ctx) : ILedgerRepository
{
    private static readonly Dictionary<string, (string Label, string Sign)> TypeMeta = new()
    {
        ["earning_hold"]             = ("Ganho pendente",               "+"),
        ["earning_released"]         = ("Ganho liberado",               "+"),
        ["earning_dispute_hold"]     = ("Congelado (disputa)",          "-"),
        ["earning_dispute_released"] = ("Liberado (disputa resolvida)", "+"),
        ["earning_dispute_refunded"] = ("Estornado (disputa)",          "-"),
        ["refund"]                   = ("Estorno",                      "-"),
        ["platform_fee"]             = ("Taxa da plataforma",           "-"),
    };

    public async Task AddAsync(LedgerEntry entry, CancellationToken ct)
    {
        ctx.LedgerEntries.Add(entry);
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<WalletBalance> GetBalanceAsync(string professionalId, CancellationToken ct)
    {
        var entries = await ctx.LedgerEntries
            .AsNoTracking()
            .Where(e => e.ProfessionalId == professionalId)
            .Select(e => new { e.Type, e.AmountCents, e.CreatedAt })
            .ToListAsync(ct);

        var pendingCents = entries.Sum(e => e.Type switch
        {
            "earning_hold" => e.AmountCents,
            "earning_released" or "earning_dispute_hold" or "refund" => -e.AmountCents,
            _ => 0
        });

        var availableCents = entries.Sum(e => e.Type switch
        {
            "earning_released"         => e.AmountCents,
            "earning_dispute_hold"     => -e.AmountCents,
            "earning_dispute_released" => e.AmountCents,
            "earning_dispute_refunded" => -e.AmountCents,
            _ => 0
        });

        var totalEarnedCents = entries
            .Where(e => e.Type == "earning_released")
            .Sum(e => e.AmountCents);

        var disputedCents = entries.Sum(e => e.Type switch
        {
            "earning_dispute_hold"                                     => e.AmountCents,
            "earning_dispute_released" or "earning_dispute_refunded"   => -e.AmountCents,
            _ => 0
        });

        var lastUpdatedAt = entries.Count > 0
            ? entries.Max(e => e.CreatedAt)
            : (DateTime?)null;

        var mpConnected = await ctx.ProfessionalMpAccounts
            .AsNoTracking()
            .AnyAsync(x => x.ProfessionalId == professionalId && x.Status == "active", ct);

        return new WalletBalance(
            ProfessionalId:  professionalId,
            PendingCents:    pendingCents,
            AvailableCents:  availableCents,
            DisputedCents:   disputedCents,
            TotalEarnedCents: totalEarnedCents,
            Currency:        "BRL",
            MpConnected:     mpConnected,
            LastUpdatedAt:   lastUpdatedAt
        );
    }

    public async Task<(IReadOnlyList<LedgerEntryDetail> Items, int Total)> GetLedgerAsync(
        string professionalId, int page, int pageSize,
        DateTime? from, DateTime? to, string? type, CancellationToken ct)
    {
        var baseQuery = ctx.LedgerEntries
            .AsNoTracking()
            .Where(e => e.ProfessionalId == professionalId);

        if (from.HasValue)
            baseQuery = baseQuery.Where(e => e.CreatedAt >= from.Value);
        if (to.HasValue)
            baseQuery = baseQuery.Where(e => e.CreatedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(type))
            baseQuery = baseQuery.Where(e => e.Type == type);

        var total = await baseQuery.CountAsync(ct);

        var pagedEntries = await baseQuery
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        if (pagedEntries.Count == 0)
            return (Array.Empty<LedgerEntryDetail>(), total);

        var orderIds = pagedEntries
            .Where(e => e.OrderId != null)
            .Select(e => e.OrderId!)
            .Distinct()
            .ToList();

        // Enrich with order/service/client data
        var enrichments = new Dictionary<string, (string? OrderStatus, string? ServiceName, string? ClientName)>();
        if (orderIds.Count > 0)
        {
            var rows = await (
                from o in ctx.Orders.AsNoTracking()
                where orderIds.Contains(o.Id)
                join s in ctx.Services.AsNoTracking() on o.ServiceId equals s.Id into services
                from s in services.DefaultIfEmpty()
                join u in ctx.Users.AsNoTracking() on o.ClientId equals u.Id into users
                from u in users.DefaultIfEmpty()
                select new
                {
                    OrderId     = o.Id,
                    OrderStatus = o.Status,
                    ServiceName = s != null ? s.Name : null,
                    ClientName  = u != null ? u.Name : null,
                }
            ).ToListAsync(ct);

            foreach (var r in rows)
                enrichments[r.OrderId] = (r.OrderStatus, r.ServiceName, r.ClientName);
        }

        var details = pagedEntries.Select(le =>
        {
            var (label, sign) = TypeMeta.TryGetValue(le.Type, out var meta) ? meta : (le.Type, "+");
            enrichments.TryGetValue(le.OrderId ?? string.Empty, out var enrich);
            return new LedgerEntryDetail(
                Id:          le.Id,
                Type:        le.Type,
                TypeLabel:   label,
                AmountCents: le.AmountCents,
                Sign:        sign,
                OrderId:     le.OrderId,
                OrderStatus: enrich.OrderStatus,
                ServiceName: enrich.ServiceName,
                ClientName:  enrich.ClientName,
                CreatedAt:   le.CreatedAt
            );
        }).ToList();

        return (details, total);
    }

    public async Task<IReadOnlyList<MonthlySummary>> GetMonthlySummaryAsync(
        string professionalId, int months, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-months);

        var data = await ctx.LedgerEntries
            .AsNoTracking()
            .Where(e => e.ProfessionalId == professionalId
                     && e.Type == "earning_released"
                     && e.CreatedAt >= cutoff)
            .Select(e => new { e.AmountCents, e.OrderId, e.CreatedAt })
            .ToListAsync(ct);

        return data
            .GroupBy(e => new DateTime(e.CreatedAt.Year, e.CreatedAt.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new MonthlySummary(
                Month:         g.Key.ToString("yyyy-MM"),
                EarnedCents:   g.Sum(e => e.AmountCents),
                ServicesCount: g.Select(e => e.OrderId).Distinct().Count()
            ))
            .ToList();
    }
}
