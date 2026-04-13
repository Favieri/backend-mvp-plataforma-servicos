using Domain.Entities;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests.Repositories;

public sealed class AvailabilityRepositoryTests : RepositoryTestBase
{
    private readonly AvailabilityRepository _repo;

    public AvailabilityRepositoryTests()
    {
        _repo = new AvailabilityRepository(Ctx);
        SeedFixtures().GetAwaiter().GetResult();
    }

    private async Task SeedFixtures()
    {
        Ctx.Users.Add(new User("pro-user", "Pro", "pro@test.com", null, "PROFESSIONAL", null, DateTime.UtcNow));
        Ctx.Professionals.Add(new Professional("pro1", "pro-user", null, null, true, null, null, 0, 60, 120, 30, false));
        await Ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task ProfessionalExistsAsync_ReturnsTrue_WhenExists()
    {
        var result = await _repo.ProfessionalExistsAsync("pro1", CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task SaveAllAsync_InsertsRows()
    {
        var rows = new List<(int Weekday, int StartMinutes, int EndMinutes, bool Active)>
        {
            (1, 480, 1020, true),
            (2, 480, 1020, true)
        };

        await _repo.SaveAllAsync("pro1", rows, CancellationToken.None);

        var saved = await Ctx.ProfessionalAvailabilities
            .AsNoTracking()
            .Where(a => a.ProfessionalId == "pro1")
            .ToListAsync();

        Assert.Equal(2, saved.Count);
    }

    [Fact]
    public async Task SaveAllAsync_ReplacesExistingRows()
    {
        var first = new List<(int, int, int, bool)> { (1, 480, 1020, true), (2, 480, 1020, true) };
        await _repo.SaveAllAsync("pro1", first, CancellationToken.None);

        var second = new List<(int, int, int, bool)> { (3, 540, 1080, false) };
        await _repo.SaveAllAsync("pro1", second, CancellationToken.None);

        var saved = await Ctx.ProfessionalAvailabilities
            .AsNoTracking()
            .Where(a => a.ProfessionalId == "pro1")
            .ToListAsync();

        Assert.Single(saved);
        Assert.Equal(3, saved[0].Weekday);
    }

    [Fact]
    public async Task CreateBlockAsync_PersistsBlock()
    {
        var startsAt = DateTime.UtcNow.AddDays(1);
        var endsAt = startsAt.AddHours(2);

        var block = await _repo.CreateBlockAsync("pro1", startsAt, endsAt, "Feriado", CancellationToken.None);
        Assert.NotNull(block);
    }

    [Fact]
    public async Task GetAvailabilityForDayAsync_ReturnsActiveRows()
    {
        var rows = new List<(int, int, int, bool)> { (1, 480, 1020, true), (1, 1020, 1200, false) };
        await _repo.SaveAllAsync("pro1", rows, CancellationToken.None);

        var result = await _repo.GetAvailabilityForDayAsync("pro1", 1, CancellationToken.None);
        Assert.Single(result);
        Assert.True(result[0].Active);
    }

    [Fact]
    public async Task GetProfessionalSchedulingConfigAsync_ReturnsConfig()
    {
        var config = await _repo.GetProfessionalSchedulingConfigAsync("pro1", CancellationToken.None);
        Assert.NotNull(config);
    }

    [Fact]
    public async Task GetProfessionalServiceDurationAsync_ReturnsDuration_WhenSet()
    {
        Ctx.Services.Add(new Service("svc1", "Test", null, DateTime.UtcNow));
        Ctx.ProfessionalServices.Add(new ProfessionalService(
            "ps1", "pro1", "svc1", "Teste", (double?)100, null,
            DurationMinutes: 45));
        await Ctx.SaveChangesAsync();

        var result = await _repo.GetProfessionalServiceDurationAsync("ps1", CancellationToken.None);
        Assert.Equal(45, result);
    }

    [Fact]
    public async Task GetProfessionalServiceDurationAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _repo.GetProfessionalServiceDurationAsync("nonexistent", CancellationToken.None);
        Assert.Null(result);
    }
}
