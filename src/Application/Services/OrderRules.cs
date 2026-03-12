using Domain.Enums;

namespace Application.Services;

/// <summary>
/// Phase 1 state machine for Order transitions, with actor-based validation.
/// Maintains backward compatibility with legacy statuses.
/// </summary>
public static class OrderRules
{
    // Maps (current_status) -> allowed next statuses per actor role
    private static readonly Dictionary<string, Dictionary<string, IReadOnlySet<string>>> _transitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ─── Phase 1 states ─────────────────────────────────────────────
            [OrderStatus.Draft] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ActorRole.Professional] = new HashSet<string> { OrderStatus.ProposalSent, OrderStatus.CancelledProfessional },
                [ActorRole.Client]       = new HashSet<string> { OrderStatus.CancelledClient },
                [ActorRole.System]       = new HashSet<string> { OrderStatus.CancelledClient }
            },
            [OrderStatus.ProposalSent] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ActorRole.Client]       = new HashSet<string> { OrderStatus.AwaitingPayment, OrderStatus.CancelledClient, OrderStatus.Draft },
                [ActorRole.Professional] = new HashSet<string> { OrderStatus.CancelledProfessional },
                [ActorRole.System]       = new HashSet<string> { OrderStatus.CancelledClient }
            },
            [OrderStatus.AwaitingPayment] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ActorRole.Client]       = new HashSet<string> { OrderStatus.Scheduled, OrderStatus.CancelledClient },
                [ActorRole.Professional] = new HashSet<string> { OrderStatus.CancelledProfessional },
                [ActorRole.System]       = new HashSet<string> { OrderStatus.CancelledClient, OrderStatus.Scheduled }
            },
            [OrderStatus.Scheduled] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ActorRole.Professional] = new HashSet<string> { OrderStatus.InTransit, OrderStatus.InProgress, OrderStatus.CancelledProfessional },
                [ActorRole.Client]       = new HashSet<string> { OrderStatus.CancelledClient },
                [ActorRole.System]       = new HashSet<string> { OrderStatus.CancelledClient }
            },
            [OrderStatus.InTransit] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ActorRole.Professional] = new HashSet<string> { OrderStatus.InProgress, OrderStatus.CancelledProfessional },
                [ActorRole.Client]       = new HashSet<string> { OrderStatus.CancelledClient },
                [ActorRole.System]       = new HashSet<string> { }
            },
            [OrderStatus.InProgress] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ActorRole.Professional] = new HashSet<string> { OrderStatus.AwaitingConfirmation },
                [ActorRole.Client]       = new HashSet<string> { OrderStatus.Disputed },
                [ActorRole.System]       = new HashSet<string> { }
            },
            [OrderStatus.AwaitingConfirmation] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ActorRole.Client]       = new HashSet<string> { OrderStatus.Completed, OrderStatus.Disputed },
                [ActorRole.Professional] = new HashSet<string> { },
                [ActorRole.System]       = new HashSet<string> { OrderStatus.Completed } // auto-confirm timeout
            },
            [OrderStatus.Completed] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ActorRole.Client]       = new HashSet<string> { OrderStatus.Evaluated, OrderStatus.Rebooked },
                [ActorRole.Professional] = new HashSet<string> { OrderStatus.Evaluated },
                [ActorRole.System]       = new HashSet<string> { OrderStatus.Evaluated }
            },
            [OrderStatus.Disputed] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ActorRole.System]       = new HashSet<string> { OrderStatus.Completed, OrderStatus.Refunded },
                [ActorRole.Admin]        = new HashSet<string> { OrderStatus.Completed, OrderStatus.Refunded },
                [ActorRole.Client]       = new HashSet<string> { },
                [ActorRole.Professional] = new HashSet<string> { }
            },

            // ─── Legacy states (backward compat) ────────────────────────────
            [OrderStatus.Aberto] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ActorRole.Professional] = new HashSet<string> { OrderStatus.Confirmado, OrderStatus.Cancelado, OrderStatus.Concluido, OrderStatus.AutoConcluido },
                [ActorRole.Client]       = new HashSet<string> { OrderStatus.Cancelado },
                [ActorRole.System]       = new HashSet<string> { OrderStatus.AutoConcluido, OrderStatus.Cancelado }
            },
            [OrderStatus.Confirmado] = new(StringComparer.OrdinalIgnoreCase)
            {
                [ActorRole.Professional] = new HashSet<string> { OrderStatus.Concluido, OrderStatus.AutoConcluido, OrderStatus.Cancelado },
                [ActorRole.Client]       = new HashSet<string> { OrderStatus.Cancelado },
                [ActorRole.System]       = new HashSet<string> { OrderStatus.AutoConcluido }
            }
        };

    /// <summary>Checks if a transition is allowed for the given actor (Phase 1 + legacy).</summary>
    public static bool CanTransition(string current, string next, string actorRole)
    {
        current = current.ToLowerInvariant();
        next = next.ToLowerInvariant();

        // Terminal states — no transition allowed
        if (OrderStatus.Terminal.Contains(current) && current != OrderStatus.Completed && current != OrderStatus.Evaluated)
            return false;

        if (!_transitions.TryGetValue(current, out var byActor))
            return false;

        if (!byActor.TryGetValue(actorRole, out var allowed))
            return false;

        return allowed.Contains(next);
    }

    /// <summary>Legacy overload (no actor validation) — backward compatible.</summary>
    public static bool CanTransition(string current, string next)
    {
        current = current.ToLowerInvariant();
        next = next.ToLowerInvariant();

        if (current == OrderStatus.Cancelado) return false;
        if (current == OrderStatus.Concluido || current == OrderStatus.AutoConcluido)
            return next == OrderStatus.Concluido || next == OrderStatus.AutoConcluido;

        return next is OrderStatus.Confirmado or OrderStatus.Cancelado
            or OrderStatus.Concluido or OrderStatus.AutoConcluido;
    }

    /// <summary>Returns all statuses that allow the given actor to transition from the given state.</summary>
    public static IReadOnlySet<string> GetAllowedTransitions(string current, string actorRole)
    {
        if (_transitions.TryGetValue(current.ToLowerInvariant(), out var byActor) &&
            byActor.TryGetValue(actorRole, out var allowed))
            return allowed;
        return new HashSet<string>();
    }

    public static bool IsTerminal(string status)
        => OrderStatus.Terminal.Contains(status.ToLowerInvariant());
}
