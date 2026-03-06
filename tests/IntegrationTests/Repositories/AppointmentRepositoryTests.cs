using Domain.Entities;
using Infrastructure.Repositories;
using Xunit;

namespace IntegrationTests.Repositories;

public sealed class AppointmentRepositoryTests : RepositoryTestBase
{
    private readonly AppointmentRepository _repo;

    public AppointmentRepositoryTests()
    {
        _repo = new AppointmentRepository(Ctx);
        SeedFixtures().GetAwaiter().GetResult();
    }

    private async Task SeedFixtures()
    {
        Ctx.Users.Add(new User("pro-user", "Pro", "pro@test.com", null, "PROFESSIONAL", null, DateTime.UtcNow));
        Ctx.Professionals.Add(new Professional("pro1", "pro-user", null, null, true, null, null, 0, 60, 120, 30, false));
        Ctx.Users.Add(new User("client1", "Client", "client@test.com", null, "CLIENT", null, DateTime.UtcNow));
        await Ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task ProfessionalExistsAsync_ReturnsTrue_WhenExists()
    {
        var result = await _repo.ProfessionalExistsAsync("pro1", CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task ProfessionalExistsAsync_ReturnsFalse_WhenNotExists()
    {
        var result = await _repo.ProfessionalExistsAsync("nope", CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task CreateAsync_PersistsAppointment()
    {
        var starts = DateTime.UtcNow.AddDays(1);
        var ends = starts.AddHours(1);

        var input = new Appointment(string.Empty, "pro1", "client1", null, starts, ends, "PENDING", null, null);
        var result = await _repo.CreateAsync(input, CancellationToken.None);

        Assert.NotEmpty(result.Id);
        Assert.Equal("pro1", result.ProfessionalId);
        Assert.Equal("PENDING", result.Status);
    }

    [Fact]
    public async Task HasConflictAsync_ReturnsTrue_WhenOverlap()
    {
        var starts = DateTime.UtcNow.AddDays(1);
        var ends = starts.AddHours(1);
        var input = new Appointment(string.Empty, "pro1", "client1", null, starts, ends, "PENDING", null, null);
        await _repo.CreateAsync(input, CancellationToken.None);

        var conflict = await _repo.HasConflictAsync("pro1", starts.AddMinutes(15), ends.AddMinutes(15), CancellationToken.None);
        Assert.True(conflict);
    }

    [Fact]
    public async Task HasConflictAsync_ReturnsFalse_WhenNoOverlap()
    {
        var starts = DateTime.UtcNow.AddDays(1);
        var ends = starts.AddHours(1);
        var input = new Appointment(string.Empty, "pro1", "client1", null, starts, ends, "PENDING", null, null);
        await _repo.CreateAsync(input, CancellationToken.None);

        var conflict = await _repo.HasConflictAsync("pro1", ends, ends.AddHours(1), CancellationToken.None);
        Assert.False(conflict);
    }

    [Fact]
    public async Task UpdateStatusAsync_ChangesStatus()
    {
        var starts = DateTime.UtcNow.AddDays(1);
        var ends = starts.AddHours(1);
        var input = new Appointment(string.Empty, "pro1", "client1", null, starts, ends, "PENDING", null, null);
        var created = await _repo.CreateAsync(input, CancellationToken.None);

        var updated = await _repo.UpdateStatusAsync(created.Id, "CONFIRMED", CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("CONFIRMED", updated!.Status);
    }

    [Fact]
    public async Task GetByClientAsync_ReturnsSortedDescByStartsAt()
    {
        var now = DateTime.UtcNow;
        var a1 = new Appointment(string.Empty, "pro1", "client1", null, now.AddDays(1), now.AddDays(1).AddHours(1), "PENDING", null, null);
        var a2 = new Appointment(string.Empty, "pro1", "client1", null, now.AddDays(2), now.AddDays(2).AddHours(1), "PENDING", null, null);
        await _repo.CreateAsync(a1, CancellationToken.None);
        await _repo.CreateAsync(a2, CancellationToken.None);

        var result = await _repo.GetByClientAsync("client1", CancellationToken.None);
        Assert.Equal(2, result.Count);
        Assert.True(result[0].StartsAt > result[1].StartsAt);
    }

    [Fact]
    public async Task GetProfessionalConfigAsync_ReturnsConfig()
    {
        var (slotMinutes, allowInstant) = await _repo.GetProfessionalConfigAsync("pro1", CancellationToken.None);
        Assert.Equal(60, slotMinutes);
        Assert.False(allowInstant);
    }
}
