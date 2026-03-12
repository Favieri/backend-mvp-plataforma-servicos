using Domain.Entities;
using Domain.Enums;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests.Repositories;

public sealed class OrderRepositoryTests : RepositoryTestBase
{
    private readonly OrderRepository _repo;

    public OrderRepositoryTests()
    {
        _repo = new OrderRepository(Ctx);
        SeedFixtures().GetAwaiter().GetResult();
    }

    private async Task SeedFixtures()
    {
        Ctx.Users.Add(new User("client1", "Client", "client@test.com", null, "CLIENT", null, DateTime.UtcNow));
        Ctx.Services.Add(new Service("svc1", "Limpeza", null, DateTime.UtcNow));
        await Ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAsync_PersistsOrder()
    {
        var order = await _repo.CreateAsync("client1", "svc1", "desc", "loc", null, CancellationToken.None);

        Assert.NotNull(order.Id);
        Assert.Equal("client1", order.ClientId);
        Assert.Equal("svc1", order.ServiceId);
        Assert.Equal(OrderStatus.Aberto, order.Status);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsOrder_WhenExists()
    {
        var order = await _repo.CreateAsync("client1", "svc1", null, null, null, CancellationToken.None);

        var found = await _repo.GetByIdAsync(order.Id, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(order.Id, found!.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        var found = await _repo.GetByIdAsync("nonexistent", CancellationToken.None);
        Assert.Null(found);
    }

    [Fact]
    public async Task CompleteOrderAsync_UpdatesStatus()
    {
        var order = await _repo.CreateAsync("client1", "svc1", null, null, null, CancellationToken.None);

        await _repo.CompleteOrderAsync(order.Id, CancellationToken.None);

        // Re-fetch to verify (clear tracking)
        Ctx.ChangeTracker.Clear();
        var updated = await Ctx.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Concluido, updated.Status);
    }

    [Fact]
    public async Task GetOrdersAsync_FiltersBy_ServiceId()
    {
        Ctx.Services.Add(new Service("svc2", "Pintura", null, DateTime.UtcNow));
        await Ctx.SaveChangesAsync();

        await _repo.CreateAsync("client1", "svc1", null, null, null, CancellationToken.None);
        await _repo.CreateAsync("client1", "svc2", null, null, null, CancellationToken.None);

        var result = await _repo.GetOrdersAsync("svc1", null, null, false, CancellationToken.None);
        Assert.All(result, o => Assert.Equal("svc1", o.ServiceId));
    }

    [Fact]
    public async Task GetMineAsync_ReturnsOnlyClientOrders()
    {
        Ctx.Users.Add(new User("client2", "Other", "other@test.com", null, "CLIENT", null, DateTime.UtcNow));
        await Ctx.SaveChangesAsync();

        await _repo.CreateAsync("client1", "svc1", null, null, null, CancellationToken.None);
        await _repo.CreateAsync("client2", "svc1", null, null, null, CancellationToken.None);

        var result = await _repo.GetMineAsync("client1", CancellationToken.None);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetOrdersAsync_OrderedByCreatedAtDesc()
    {
        await _repo.CreateAsync("client1", "svc1", "first", null, null, CancellationToken.None);
        await Task.Delay(10); // ensure time difference
        await _repo.CreateAsync("client1", "svc1", "second", null, null, CancellationToken.None);

        var result = await _repo.GetOrdersAsync(null, null, null, false, CancellationToken.None);
        Assert.True(result[0].CreatedAt >= result[1].CreatedAt);
    }

    // ─── Phase 1 Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateBookingAsync_PersistsBookingOrder()
    {
        var order = Order.CreateBooking(
            id: Guid.NewGuid().ToString(),
            clientId: "client1",
            professionalId: "pro1",
            serviceId: "svc1",
            tierId: 1,
            priceTotalCents: 10000,
            signalCents: 0,
            balanceCents: 10000,
            installments: 1,
            paymentMethod: "pix",
            scope: "Limpeza completa",
            scheduledAt: DateTime.UtcNow.AddDays(3));

        var created = await _repo.CreateBookingAsync(order, CancellationToken.None);

        Assert.Equal(OrderStatus.AwaitingPayment, created.Status);
        Assert.Equal("pro1", created.ProfessionalId);
        Assert.Equal(1, created.TierId);
        Assert.Equal(OrderOrigin.BookingDirect, created.Origin);
        Assert.Equal(10000, created.PriceTotalCents);
    }

    [Fact]
    public async Task MarkAwaitingConfirmationAsync_SetsAutoConfirmAt()
    {
        var order = await _repo.CreateAsync("client1", "svc1", null, null, null, CancellationToken.None);

        var ok = await _repo.MarkAwaitingConfirmationAsync(order.Id, 72, CancellationToken.None);

        Assert.True(ok);
        Ctx.ChangeTracker.Clear();
        var updated = await Ctx.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.AwaitingConfirmation, updated.Status);
        Assert.NotNull(updated.AutoConfirmAt);
        Assert.True(updated.AutoConfirmAt!.Value > DateTime.UtcNow.AddHours(71));
    }

    [Fact]
    public async Task MarkCompletedAsync_SetsCompletedAt_AndClearsAutoConfirm()
    {
        var order = await _repo.CreateAsync("client1", "svc1", null, null, null, CancellationToken.None);
        await _repo.MarkAwaitingConfirmationAsync(order.Id, 72, CancellationToken.None);

        var ok = await _repo.MarkCompletedAsync(order.Id, CancellationToken.None);

        Assert.True(ok);
        Ctx.ChangeTracker.Clear();
        var updated = await Ctx.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Completed, updated.Status);
        Assert.NotNull(updated.CompletedAt);
        Assert.Null(updated.AutoConfirmAt);
    }

    [Fact]
    public async Task MarkCancelledByClientAsync_SetsCancelledFields()
    {
        var order = await _repo.CreateAsync("client1", "svc1", null, null, null, CancellationToken.None);

        var ok = await _repo.MarkCancelledByClientAsync(order.Id, "Mudança de planos", CancellationToken.None);

        Assert.True(ok);
        Ctx.ChangeTracker.Clear();
        var updated = await Ctx.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.CancelledClient, updated.Status);
        Assert.NotNull(updated.CancelledAt);
        Assert.Equal(ActorRole.Client, updated.CancelledBy);
        Assert.Equal("Mudança de planos", updated.CancellationReason);
    }

    [Fact]
    public async Task GetMineByRoleAsync_ReturnsClientOrders()
    {
        var booking = Order.CreateBooking(
            Guid.NewGuid().ToString(), "client1", "pro1", "svc1", 1,
            5000, 0, 5000, 1, null, null, null);
        await _repo.CreateBookingAsync(booking, CancellationToken.None);

        var result = await _repo.GetMineByRoleAsync("client1", "client", CancellationToken.None);
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public async Task GetOrdersAwaitingAutoConfirmAsync_ReturnsOrdersPastDeadline()
    {
        var order = await _repo.CreateAsync("client1", "svc1", null, null, null, CancellationToken.None);
        // Manually set autoConfirmAt to the past via ExecuteUpdate
        await Ctx.Orders
            .Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.AwaitingConfirmation)
                .SetProperty(o => o.AutoConfirmAt, DateTime.UtcNow.AddHours(-1)));

        var results = await _repo.GetOrdersAwaitingAutoConfirmAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.Contains(results, o => o.Id == order.Id);
    }
}
