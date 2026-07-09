using Domain.Entities;
using Infrastructure.Repositories;
using Xunit;

namespace IntegrationTests.Repositories;

public sealed class ProfessionalReadRepositoryTests : RepositoryTestBase
{
    private readonly ProfessionalReadRepository _repo;

    public ProfessionalReadRepositoryTests()
    {
        _repo = new ProfessionalReadRepository(Ctx);
    }

    private async Task SeedProfessionalsAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var userId = $"user{i}";
            Ctx.Users.Add(new User(userId, $"Pro {i}", $"pro{i}@test.com", null, "PROFESSIONAL", null, DateTime.UtcNow));
            Ctx.Professionals.Add(new Professional(
                Id: $"pro{i}",
                UserId: userId,
                Bio: null,
                Rating: i % 5,
                Active: true,
                AvatarUrl: null,
                AvailabilityText: null,
                CompletedJobsCount: 0,
                SlotMinutes: null,
                LeadTimeMinutes: null,
                MaxAdvanceDays: null,
                AllowInstantBooking: null));
        }
        await Ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task GetProfessionalsAsync_Page1_ReturnsDefaultPageSize_WithCorrectTotalCount()
    {
        await SeedProfessionalsAsync(25);

        var page1 = await _repo.GetProfessionalsAsync(null, null, null, page: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal(20, page1.Items.Count);
        Assert.Equal(25, page1.TotalCount);
    }

    [Fact]
    public async Task GetProfessionalsAsync_Page2_ReturnsRemainder_WithNoOverlapWithPage1()
    {
        await SeedProfessionalsAsync(25);

        var page1 = await _repo.GetProfessionalsAsync(null, null, null, page: 1, pageSize: 20, CancellationToken.None);
        var page2 = await _repo.GetProfessionalsAsync(null, null, null, page: 2, pageSize: 20, CancellationToken.None);

        Assert.Equal(5, page2.Items.Count);
        Assert.Equal(25, page2.TotalCount);

        var page1Ids = page1.Items.Select(p => p.Id).ToHashSet();
        var page2Ids = page2.Items.Select(p => p.Id).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));
    }

    [Fact]
    public async Task GetProfessionalsFilteredAsync_ReturnsEmpty_WhenNoMatches()
    {
        await SeedProfessionalsAsync(3);

        var result = await _repo.GetProfessionalsFilteredAsync(
            zoneId: null, serviceId: null, verificationStatus: "verified", minRating: null,
            professionalId: null, page: 1, pageSize: 20, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetProfessionalsAsync_ReturnsReviewCount_MatchingReviewsPerProfessional()
    {
        await SeedProfessionalsAsync(2);

        Ctx.Reviews.Add(new Review("rev1", "order1", "pro0", "client1", 5, null, DateTime.UtcNow));
        Ctx.Reviews.Add(new Review("rev2", "order2", "pro0", "client1", 4, null, DateTime.UtcNow));
        Ctx.Reviews.Add(new Review("rev3", "order3", "pro1", "client1", 3, null, DateTime.UtcNow));
        await Ctx.SaveChangesAsync();

        var result = await _repo.GetProfessionalsAsync(null, null, null, page: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal(2, result.Items.Single(p => p.Id == "pro0").ReviewCount);
        Assert.Equal(1, result.Items.Single(p => p.Id == "pro1").ReviewCount);
    }

    [Fact]
    public async Task GetProfessionalsAsync_ReviewCount_IsZero_WhenNoReviews()
    {
        await SeedProfessionalsAsync(1);

        var result = await _repo.GetProfessionalsAsync(null, null, null, page: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal(0, result.Items.Single().ReviewCount);
    }
}
