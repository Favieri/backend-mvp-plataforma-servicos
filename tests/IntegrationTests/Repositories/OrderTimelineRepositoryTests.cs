using Domain.Entities;
using Domain.Enums;
using Infrastructure.Repositories;
using Xunit;

namespace IntegrationTests.Repositories;

public sealed class OrderTimelineRepositoryTests : RepositoryTestBase
{
    private readonly OrderTimelineRepository _repo;
    private readonly OrderRepository _orderRepo;

    public OrderTimelineRepositoryTests()
    {
        _repo = new OrderTimelineRepository(Ctx);
        _orderRepo = new OrderRepository(Ctx);
        SeedFixtures().GetAwaiter().GetResult();
    }

    private async Task SeedFixtures()
    {
        Ctx.Users.Add(new User("client1", "Client", "client@test.com", null, "CLIENT", null, DateTime.UtcNow));
        Ctx.Services.Add(new Service("svc1", "Pintura", null, DateTime.UtcNow));
        await Ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task AddEventAsync_PersistsEvent()
    {
        var order = await _orderRepo.CreateAsync("client1", "svc1", null, null, null, CancellationToken.None);

        var timeline = OrderTimeline.Create(
            id: Guid.NewGuid().ToString(),
            orderId: order.Id,
            eventType: "order_created",
            actorId: "client1",
            actorRole: ActorRole.Client);

        await _repo.AddEventAsync(timeline, CancellationToken.None);

        var events = await _repo.GetByOrderIdAsync(order.Id, CancellationToken.None);
        Assert.Single(events);
        Assert.Equal("order_created", events[0].EventType);
        Assert.Equal("client1", events[0].ActorId);
        Assert.Equal(ActorRole.Client, events[0].ActorRole);
    }

    [Fact]
    public async Task GetByOrderIdAsync_ReturnsEventsInChronologicalOrder()
    {
        var order = await _orderRepo.CreateAsync("client1", "svc1", null, null, null, CancellationToken.None);

        await _repo.AddEventAsync(OrderTimeline.Create(
            Guid.NewGuid().ToString(), order.Id, "first_event"), CancellationToken.None);
        await Task.Delay(10); // ensure time difference
        await _repo.AddEventAsync(OrderTimeline.Create(
            Guid.NewGuid().ToString(), order.Id, "second_event"), CancellationToken.None);

        var events = await _repo.GetByOrderIdAsync(order.Id, CancellationToken.None);
        Assert.Equal(2, events.Count);
        Assert.True(events[0].CreatedAt <= events[1].CreatedAt);
        Assert.Equal("first_event", events[0].EventType);
        Assert.Equal("second_event", events[1].EventType);
    }

    [Fact]
    public async Task GetByOrderIdAsync_ReturnsEmpty_WhenNoEvents()
    {
        var events = await _repo.GetByOrderIdAsync("nonexistent", CancellationToken.None);
        Assert.Empty(events);
    }

    [Fact]
    public async Task AddEventAsync_WithMetadata_PersistsMetadata()
    {
        var order = await _orderRepo.CreateAsync("client1", "svc1", null, null, null, CancellationToken.None);
        var metadata = "{\"reason\":\"test\"}";

        await _repo.AddEventAsync(OrderTimeline.Create(
            Guid.NewGuid().ToString(), order.Id, "status_changed",
            actorId: "pro1", actorRole: ActorRole.Professional, metadata: metadata), CancellationToken.None);

        var events = await _repo.GetByOrderIdAsync(order.Id, CancellationToken.None);
        Assert.Equal(metadata, events[0].Metadata);
    }
}
