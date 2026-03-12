using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ProposalRepository(AppDbContext ctx) : IProposalRepository
{
    public async Task<Proposal?> GetByIdAsync(string id, CancellationToken ct)
        => await ctx.Proposals.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Proposal> CreateAsync(Proposal proposal, CancellationToken ct)
    {
        ctx.Proposals.Add(proposal);
        await ctx.SaveChangesAsync(ct);
        return proposal;
    }

    public async Task<bool> SendAsync(string proposalId, CancellationToken ct)
    {
        var proposal = await ctx.Proposals.FirstOrDefaultAsync(p => p.Id == proposalId, ct);
        if (proposal is null) return false;
        proposal.Send();
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> AcceptAsync(string proposalId, string orderId, CancellationToken ct)
    {
        var proposal = await ctx.Proposals.FirstOrDefaultAsync(p => p.Id == proposalId, ct);
        if (proposal is null) return false;
        proposal.Accept(orderId);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RejectAsync(string proposalId, string? reason, CancellationToken ct)
    {
        var proposal = await ctx.Proposals.FirstOrDefaultAsync(p => p.Id == proposalId, ct);
        if (proposal is null) return false;
        proposal.Reject(reason);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> StartNegotiationAsync(string proposalId, CancellationToken ct)
    {
        var proposal = await ctx.Proposals.FirstOrDefaultAsync(p => p.Id == proposalId, ct);
        if (proposal is null) return false;
        proposal.StartNegotiation();
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<Proposal>> GetByConversationAsync(string conversationId, CancellationToken ct)
        => await ctx.Proposals
            .AsNoTracking()
            .Where(p => p.ConversationId == conversationId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<object>> GetMineAsync(string userId, string role, CancellationToken ct)
    {
        var query = role == "professional"
            ? ctx.Proposals.AsNoTracking().Where(p => p.ProfessionalId == userId)
            : ctx.Proposals.AsNoTracking().Where(p => p.ClientId == userId);

        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                id = p.Id,
                serviceId = p.ServiceId,
                priceTotalCents = p.PriceTotalCents,
                durationEstimate = p.DurationEstimate,
                validUntil = p.ValidUntil,
                status = p.Status,
                createdAt = p.CreatedAt
            })
            .ToListAsync(ct);

        return rows.Cast<object>().ToList();
    }

    public async Task<int> ExpireOverdueAsync(DateTime before, CancellationToken ct)
    {
        var expired = await ctx.Proposals
            .Where(p => p.ValidUntil < before &&
                        (p.Status == Domain.Enums.ProposalStatus.Draft ||
                         p.Status == Domain.Enums.ProposalStatus.Sent ||
                         p.Status == Domain.Enums.ProposalStatus.Negotiating))
            .ToListAsync(ct);

        foreach (var p in expired) p.Expire();
        await ctx.SaveChangesAsync(ct);
        return expired.Count;
    }
}
