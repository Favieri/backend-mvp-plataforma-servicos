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
        var rows = await ctx.Proposals
            .Where(p => p.Id == proposalId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, Domain.Enums.ProposalStatus.Sent)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);
        return rows > 0;
    }

    public async Task<bool> AcceptAsync(string proposalId, string orderId, CancellationToken ct)
    {
        var rows = await ctx.Proposals
            .Where(p => p.Id == proposalId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, Domain.Enums.ProposalStatus.Accepted)
                .SetProperty(p => p.OrderId, orderId)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);
        return rows > 0;
    }

    public async Task<bool> RejectAsync(string proposalId, string? reason, CancellationToken ct)
    {
        var rows = await ctx.Proposals
            .Where(p => p.Id == proposalId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, Domain.Enums.ProposalStatus.Rejected)
                .SetProperty(p => p.RejectionReason, reason)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);
        return rows > 0;
    }

    public async Task<bool> StartNegotiationAsync(string proposalId, CancellationToken ct)
    {
        var rows = await ctx.Proposals
            .Where(p => p.Id == proposalId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, Domain.Enums.ProposalStatus.Negotiating)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);
        return rows > 0;
    }

    public async Task<IReadOnlyList<Proposal>> GetByConversationAsync(string conversationId, CancellationToken ct)
        => await ctx.Proposals
            .AsNoTracking()
            .Where(p => p.ConversationId == conversationId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<object>> GetMineAsync(string userId, string role, CancellationToken ct)
    {
        // When role=professional, userId is the User.Id from the JWT.
        // Proposal.ProfessionalId stores Professional.Id (a different UUID).
        // We resolve User.Id → Professional.Id via an EXISTS subquery.
        var query = role == "professional"
            ? ctx.Proposals.AsNoTracking().Where(p =>
                ctx.Professionals.Any(pr => pr.UserId == userId && pr.Id == p.ProfessionalId))
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
