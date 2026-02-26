using Domain.Enums;

namespace Application.Services;

public static class OrderRules
{
    public static bool CanTransition(string current, string next)
    {
        current = current.ToLowerInvariant();
        next = next.ToLowerInvariant();
        if (current == OrderStatus.Cancelado) return false;
        if (current == OrderStatus.Concluido || current == OrderStatus.AutoConcluido)
        {
            return next == OrderStatus.Concluido || next == OrderStatus.AutoConcluido;
        }

        return next is OrderStatus.Confirmado or OrderStatus.Cancelado or OrderStatus.Concluido or OrderStatus.AutoConcluido;
    }
}
