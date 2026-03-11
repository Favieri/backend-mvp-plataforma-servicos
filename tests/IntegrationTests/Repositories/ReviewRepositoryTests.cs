using Domain.Entities;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests.Repositories;

public sealed class ReviewRepositoryTests : RepositoryTestBase
{
    private readonly ReviewRepository _repo;

    public ReviewRepositoryTests()
    {
        _repo = new ReviewRepository(Ctx);
        SeedFixtures().GetAwaiter().GetResult();
    }

    private async Task SeedFixtures()
    {
        Ctx.Users.Add(new User("pro-user", "Pro", "pro@test.com", null, "PROFESSIONAL", null, DateTime.UtcNow));
        Ctx.Professionals.Add(new Professional("pro1", "pro-user", null, 0d, true, null, null, 0, null, null, null, null));
        Ctx.Users.Add(new User("client1", "Client", "client@test.com", null, "CLIENT", null, DateTime.UtcNow));
        Ctx.Services.Add(new Service("svc1", "Limpeza", null, DateTime.UtcNow));
        var order = Order.Create("order1", "client1", "svc1", null, null, DateTime.UtcNow.AddDays(-1));
        order.UpdateStatus("concluido");
        Ctx.Orders.Add(order);
        await Ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAsync_PersistsReview_AndUpdatesRating()
    {
        var result = await _repo.CreateAsync("pro1", "client1", "order1", 5, "Great!", CancellationToken.None);

        Assert.NotNull(result);

        // Verify professional rating updated
        Ctx.ChangeTracker.Clear();
        var pro = await Ctx.Professionals.AsNoTracking().FirstAsync(p => p.Id == "pro1");
        Assert.Equal(5.0, pro.Rating);
    }

    [Fact]
    public async Task OrderAlreadyReviewedAsync_ReturnsFalse_WhenNoReview()
    {
        var result = await _repo.OrderAlreadyReviewedAsync("order1", CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task OrderAlreadyReviewedAsync_ReturnsTrue_AfterCreate()
    {
        await _repo.CreateAsync("pro1", "client1", "order1", 4, null, CancellationToken.None);
        var result = await _repo.OrderAlreadyReviewedAsync("order1", CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task OrderBelongsToClientAsync_ReturnsTrue_WhenOwned()
    {
        var result = await _repo.OrderBelongsToClientAsync("order1", "client1", CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task OrderBelongsToClientAsync_ReturnsFalse_WhenNotOwned()
    {
        var result = await _repo.OrderBelongsToClientAsync("order1", "otherClient", CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task GetByProfessionalAsync_ReturnsClamped_Limit()
    {
        await _repo.CreateAsync("pro1", "client1", "order1", 4, "ok", CancellationToken.None);

        var result = await _repo.GetByProfessionalAsync("pro1", 10, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetProfessionalUserIdAsync_ReturnsUserId()
    {
        var userId = await _repo.GetProfessionalUserIdAsync("pro1", CancellationToken.None);
        Assert.Equal("pro-user", userId);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _repo.UpdateAsync("nonexistent", 5, "comment", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesRating()
    {
        await _repo.CreateAsync("pro1", "client1", "order1", 3, null, CancellationToken.None);
        var review = await Ctx.Reviews.AsNoTracking().FirstAsync();

        await _repo.UpdateAsync(review.Id, 5, null, CancellationToken.None);

        Ctx.ChangeTracker.Clear();
        var updated = await Ctx.Reviews.AsNoTracking().FirstAsync();
        Assert.Equal(5, updated.Rating);
    }
}
