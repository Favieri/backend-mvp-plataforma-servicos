using Domain.Entities;
using Infrastructure.Repositories;
using Xunit;

namespace IntegrationTests.Repositories;

public sealed class UserRepositoryTests : RepositoryTestBase
{
    private readonly UserRepository _repo;

    public UserRepositoryTests()
    {
        _repo = new UserRepository(Ctx);
    }

    [Fact]
    public async Task EmailExistsAsync_ReturnsFalse_WhenNoUser()
    {
        var result = await _repo.EmailExistsAsync("notfound@test.com", CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task EmailExistsAsync_ReturnsTrue_WhenUserExists()
    {
        Ctx.Users.Add(new User("u1", "Alice", "alice@test.com", null, "CLIENT", null, DateTime.UtcNow));
        await Ctx.SaveChangesAsync();

        var result = await _repo.EmailExistsAsync("alice@test.com", CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task ZoneExistsAndActiveAsync_ReturnsFalse_WhenNoZone()
    {
        var result = await _repo.ZoneExistsAndActiveAsync("z99", CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task ZoneExistsAndActiveAsync_ReturnsTrue_WhenActiveZone()
    {
        Ctx.Zones.Add(new Zone("z1", "Centro", true, DateTime.UtcNow));
        await Ctx.SaveChangesAsync();

        var result = await _repo.ZoneExistsAndActiveAsync("z1", CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task ZoneExistsAndActiveAsync_ReturnsFalse_WhenInactiveZone()
    {
        Ctx.Zones.Add(new Zone("z2", "Bairro", false, DateTime.UtcNow));
        await Ctx.SaveChangesAsync();

        var result = await _repo.ZoneExistsAndActiveAsync("z2", CancellationToken.None);
        Assert.False(result);
    }
}
