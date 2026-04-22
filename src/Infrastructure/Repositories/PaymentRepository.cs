using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class PaymentRepository(AppDbContext ctx) : IPaymentRepository
{
    public Task<Payment?> GetByIdAsync(string paymentId, CancellationToken ct) =>
        ctx.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId, ct);

    public Task<Payment?> GetByOrderIdAsync(string orderId, CancellationToken ct) =>
        ctx.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<Payment?> GetPendingByOrderIdAsync(string orderId, CancellationToken ct) =>
        ctx.Payments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrderId == orderId && p.Status == "pending", ct);

    public async Task CreateAsync(Payment payment, CancellationToken ct)
    {
        ctx.Payments.Add(payment);
        await ctx.SaveChangesAsync(ct);
    }

    public Task UpdateStatusAsync(string paymentId, string status, CancellationToken ct) =>
        ctx.Payments
            .Where(p => p.Id == paymentId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status), ct);

    public Task CancelPendingByOrderIdAsync(string orderId, CancellationToken ct) =>
        ctx.Payments
            .Where(p => p.OrderId == orderId && p.Status == "pending")
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "cancelled"), ct);
}
