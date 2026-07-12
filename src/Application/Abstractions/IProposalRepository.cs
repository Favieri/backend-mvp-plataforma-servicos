using Domain.Entities;

namespace Application.Abstractions;

public interface IProposalRepository
{
    Task<Proposal?> GetByIdAsync(string id, CancellationToken ct);
    Task<Proposal> CreateAsync(Proposal proposal, CancellationToken ct);
    Task<bool> SendAsync(string proposalId, CancellationToken ct);
    Task<bool> AcceptAsync(string proposalId, string orderId, CancellationToken ct);
    Task<bool> RejectAsync(string proposalId, string? reason, CancellationToken ct);
    Task<bool> StartNegotiationAsync(string proposalId, CancellationToken ct);
    Task<IReadOnlyList<Proposal>> GetByConversationAsync(string conversationId, CancellationToken ct);
    Task<IReadOnlyList<object>> GetMineAsync(string userId, string role, CancellationToken ct);
    Task<int> ExpireOverdueAsync(DateTime before, CancellationToken ct);

    // ─── Lead flow: limite do cliente ─────────────────────────────────────
    /// <summary>Conta propostas ativas (Sent/Negotiating) vinculadas a um pedido-lead de origem.</summary>
    Task<int> CountActiveBySourceOrderAsync(string sourceOrderId, CancellationToken ct);
    /// <summary>Propostas ativas (Sent/Negotiating) do mesmo lead, exceto a informada — usado para auto-rejeitar ao aceitar.</summary>
    Task<IReadOnlyList<Proposal>> GetActiveBySourceOrderAsync(string sourceOrderId, string excludeId, CancellationToken ct);
}
