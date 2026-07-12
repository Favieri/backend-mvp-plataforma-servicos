using System.Net.Http.Json;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// HTTP-level coverage for Item 1 (PRD Paginação real em GET /professionals): the endpoint must
/// never return more than the default pageSize even when no page/pageSize params are sent, and
/// must respond with the paginated envelope { items, total, page, pageSize, totalPages } used
/// elsewhere in the API (see GET /wallet/ledger).
/// Swaps AppDbContext to SQLite in-memory (same approach as RepositoryTestBase) so the test
/// doesn't require a live Postgres instance.
/// </summary>
public sealed class ProfessionalsPaginationEndpointTests : IClassFixture<ProfessionalsPaginationEndpointTests.ApiFactory>
{
    private readonly ApiFactory _factory;

    public ProfessionalsPaginationEndpointTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProfessionals_WithoutPageParams_NeverExceedsDefaultPageSize()
    {
        await _factory.SeedProfessionalsAsync(25);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/professionals");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        var items = body.GetProperty("items");
        Assert.True(items.GetArrayLength() <= 20, $"Expected at most 20 items, got {items.GetArrayLength()}");
        Assert.Equal(25, body.GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(20, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, body.GetProperty("totalPages").GetInt32());
        Assert.Equal("25", response.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task GetProfessionals_Page2_ReturnsRemainder()
    {
        await _factory.SeedProfessionalsAsync(25);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/professionals?page=2&pageSize=20");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        var items = body.GetProperty("items");
        Assert.Equal(5, items.GetArrayLength());
        Assert.Equal(25, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("page").GetInt32());
    }

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");

        public ApiFactory()
        {
            IntegrationTestDefaults.ConfigureTestEnvironment();
            _connection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
            });
        }

        public async Task SeedProfessionalsAsync(int count)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Program.cs runs Database.MigrateAsync() unconditionally at host startup, which fails
            // against SQLite (the migration history assumes Postgres tables already exist) and
            // leaves the in-memory database without tables. Force a clean rebuild from the current
            // model before seeding, mirroring RepositoryTestBase's EnsureCreated-only approach.
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();

            for (var i = 0; i < count; i++)
            {
                var userId = $"user{i}";
                ctx.Users.Add(new User(userId, $"Pro {i}", $"pro{i}@test.com", null, "PROFESSIONAL", null, DateTime.UtcNow));
                ctx.Professionals.Add(new Professional(
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
            await ctx.SaveChangesAsync();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _connection.Dispose();
            }
        }
    }
}
