using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class DisputeRepository(AppDbContext ctx) : IDisputeRepository
{
    public async Task<Dispute?> GetByIdAsync(string id, CancellationToken ct)
        => await ctx.Disputes.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<Dispute?> GetByOrderIdAsync(string orderId, CancellationToken ct)
        => await ctx.Disputes.AsNoTracking().FirstOrDefaultAsync(d => d.OrderId == orderId, ct);

    public async Task<IReadOnlyList<object>> GetByProfessionalAsync(string professionalId, CancellationToken ct)
    {
        var rows = await ctx.Disputes
            .AsNoTracking()
            .Where(d => d.ProfessionalId == professionalId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => (object)new
            {
                id = d.Id,
                orderId = d.OrderId,
                clientId = d.ClientId,
                reason = d.Reason,
                status = d.Status,
                createdAt = d.CreatedAt,
                resolvedAt = d.ResolvedAt
            })
            .ToListAsync(ct);

        return rows;
    }

    public async Task<IReadOnlyList<object>> GetByClientAsync(string clientId, CancellationToken ct)
    {
        var rows = await ctx.Disputes
            .AsNoTracking()
            .Where(d => d.ClientId == clientId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => (object)new
            {
                id = d.Id,
                orderId = d.OrderId,
                professionalId = d.ProfessionalId,
                reason = d.Reason,
                status = d.Status,
                createdAt = d.CreatedAt,
                resolvedAt = d.ResolvedAt
            })
            .ToListAsync(ct);

        return rows;
    }

    public async Task<IReadOnlyList<object>> GetAllAsync(string? status, CancellationToken ct)
    {
        var query = ctx.Disputes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(d => d.Status == status);

        var rows = await query
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => (object)new
            {
                id = d.Id,
                orderId = d.OrderId,
                professionalId = d.ProfessionalId,
                clientId = d.ClientId,
                reason = d.Reason,
                status = d.Status,
                createdAt = d.CreatedAt,
                resolvedAt = d.ResolvedAt
            })
            .ToListAsync(ct);

        return rows;
    }

    public async Task<Dispute> CreateAsync(Dispute dispute, CancellationToken ct)
    {
        ctx.Disputes.Add(dispute);
        await ctx.SaveChangesAsync(ct);
        return dispute;
    }

    public async Task<bool> AddProfessionalResponseAsync(
        string id, string response, string? evidenceUrls, CancellationToken ct)
    {
        var dispute = await ctx.Disputes.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dispute is null) return false;

        dispute.AddProfessionalResponse(response, evidenceUrls);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ResolveAsync(
        string id, string resolution, string resolvedBy, int? refundAmountCents, CancellationToken ct)
    {
        var dispute = await ctx.Disputes.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dispute is null) return false;

        dispute.Resolve(resolution, resolvedBy, refundAmountCents);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> EscalateAsync(string id, CancellationToken ct)
    {
        var dispute = await ctx.Disputes.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dispute is null) return false;

        dispute.EscalateToMediation();
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CloseAsync(string id, CancellationToken ct)
    {
        var dispute = await ctx.Disputes.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dispute is null) return false;

        dispute.Close();
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> OrderHasOpenDisputeAsync(string orderId, CancellationToken ct)
        => await ctx.Disputes
            .AsNoTracking()
            .AnyAsync(d => d.OrderId == orderId
                && d.Status != Domain.Enums.DisputeStatus.Resolved
                && d.Status != Domain.Enums.DisputeStatus.Closed, ct);
}
