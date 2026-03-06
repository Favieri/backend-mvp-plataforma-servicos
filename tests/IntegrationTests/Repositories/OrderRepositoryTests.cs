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
}
