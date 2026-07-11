using Domain.Entities;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests.Repositories;

/// <summary>
/// Regression coverage for the raw SQL used by AuthRepository.LoginAsync, which once referenced
/// a non-existent "professionals" table/columns and broke login for every role in production.
/// </summary>
public sealed class AuthRepositoryTests : RepositoryTestBase
{
    private readonly AuthRepository _repo;

    public AuthRepositoryTests()
    {
        // "Senha" is Ignore()'d on the EF model (see UserConfiguration), so EnsureCreated
        // does not create the column. AuthRepository's raw SQL still selects u.senha directly.
        Ctx.Database.ExecuteSqlRaw("ALTER TABLE \"User\" ADD COLUMN senha TEXT NOT NULL DEFAULT ''");

        // addr_* columns are not part of the User domain entity/EF model either (see
        // UserConfiguration and migration 20260320000000_AddAddressFields, which adds them via
        // raw SQL directly against Postgres). AuthRepository's raw SQL selects them for the
        // login response's defaultAddress, so the SQLite test schema needs them added manually
        // here too, the same way senha is handled above.
        Ctx.Database.ExecuteSqlRaw("ALTER TABLE \"User\" ADD COLUMN addr_zip_code TEXT");
        Ctx.Database.ExecuteSqlRaw("ALTER TABLE \"User\" ADD COLUMN addr_street TEXT");
        Ctx.Database.ExecuteSqlRaw("ALTER TABLE \"User\" ADD COLUMN addr_number TEXT");
        Ctx.Database.ExecuteSqlRaw("ALTER TABLE \"User\" ADD COLUMN addr_neighborhood TEXT");
        Ctx.Database.ExecuteSqlRaw("ALTER TABLE \"User\" ADD COLUMN addr_city TEXT");
        Ctx.Database.ExecuteSqlRaw("ALTER TABLE \"User\" ADD COLUMN addr_state TEXT");
        Ctx.Database.ExecuteSqlRaw("ALTER TABLE \"User\" ADD COLUMN addr_complement TEXT");
        Ctx.Database.ExecuteSqlRaw("ALTER TABLE \"User\" ADD COLUMN addr_reference TEXT");

        _repo = new AuthRepository(Ctx);
    }

    private void SeedUser(string id, string email, string role, string plainPassword)
    {
        Ctx.Users.Add(new User(id, "User " + id, email, null, role, null, DateTime.UtcNow));
        Ctx.SaveChangesAsync().GetAwaiter().GetResult();

        var hash = BCrypt.Net.BCrypt.HashPassword(plainPassword);
        Ctx.Database.ExecuteSqlRaw(
            "UPDATE \"User\" SET senha = {0} WHERE id = {1}", hash, id);
    }

    [Fact]
    public async Task LoginAsync_ReturnsUser_ForClienteRole()
    {
        SeedUser("u-cliente", "cliente@test.com", "cliente", "senha123");

        var result = await _repo.LoginAsync("cliente@test.com", "senha123", CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task LoginAsync_ReturnsUser_ForAdminRole()
    {
        SeedUser("u-admin", "admin@test.com", "admin", "senha123");

        var result = await _repo.LoginAsync("admin@test.com", "senha123", CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task LoginAsync_ReturnsUser_ForProfissionalRole_WithProfessionalJoin()
    {
        SeedUser("u-prof", "prof@test.com", "profissional", "senha123");

        Ctx.Professionals.Add(new Professional(
            Id: "p1",
            UserId: "u-prof",
            Bio: null,
            Rating: null,
            Active: true,
            AvatarUrl: null,
            AvailabilityText: null,
            CompletedJobsCount: 0,
            SlotMinutes: null,
            LeadTimeMinutes: null,
            MaxAdvanceDays: null,
            AllowInstantBooking: null));
        await Ctx.SaveChangesAsync();

        var result = await _repo.LoginAsync("prof@test.com", "senha123", CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task LoginAsync_ReturnsNull_WhenPasswordInvalid()
    {
        SeedUser("u-badpw", "badpw@test.com", "cliente", "senha123");

        var result = await _repo.LoginAsync("badpw@test.com", "wrong-password", CancellationToken.None);

        Assert.Null(result);
    }
}
