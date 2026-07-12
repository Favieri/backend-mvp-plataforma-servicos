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
/// Guards against divergence between GET /professionals (listing) and GET /professionals/{id}
/// (detail) for the trust fields (verificationStatus, badges, responseRate,
/// avgResponseTimeMinutes, completionRate) — see PRD-Contrato-Dados-Confianca item 1.2.
/// </summary>
public sealed class ProfessionalTrustFieldsConsistencyTests : IClassFixture<ProfessionalTrustFieldsConsistencyTests.ApiFactory>
{
    private readonly ApiFactory _factory;

    public ProfessionalTrustFieldsConsistencyTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Listing_And_Detail_Return_Identical_TrustFields_For_Same_Professional()
    {
        const string professionalId = "pro-trust-1";
        await _factory.SeedProfessionalAsync(
            id: professionalId,
            verificationStatus: "verified",
            badges: "verified,responsive",
            responseRate: 0.87,
            avgResponseTimeMinutes: 42,
            completionRate: 0.965);

        using var client = _factory.CreateClient();

        var listingResponse = await client.GetAsync("/professionals");
        listingResponse.EnsureSuccessStatusCode();
        var listingBody = await listingResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var listingItem = listingBody.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetString() == professionalId);

        var detailResponse = await client.GetAsync($"/professionals/{professionalId}");
        detailResponse.EnsureSuccessStatusCode();
        var detailBody = await detailResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal(
            listingItem.GetProperty("verificationStatus").GetString(),
            detailBody.GetProperty("verificationStatus").GetString());
        Assert.Equal(
            listingItem.GetProperty("responseRate").GetDouble(),
            detailBody.GetProperty("responseRate").GetDouble());
        Assert.Equal(
            listingItem.GetProperty("avgResponseTimeMinutes").GetInt32(),
            detailBody.GetProperty("avgResponseTimeMinutes").GetInt32());
        Assert.Equal(
            listingItem.GetProperty("completionRate").GetDouble(),
            detailBody.GetProperty("completionRate").GetDouble());

        var listingBadges = listingItem.GetProperty("badges").EnumerateArray().Select(b => b.GetString()).ToArray();
        var detailBadges = detailBody.GetProperty("badges").EnumerateArray().Select(b => b.GetString()).ToArray();
        Assert.Equal(listingBadges, detailBadges);
    }

    [Fact]
    public async Task TrustMetricsEndpoint_Matches_Listing_And_Detail()
    {
        const string professionalId = "pro-trust-2";
        await _factory.SeedProfessionalAsync(
            id: professionalId,
            verificationStatus: "pending",
            badges: null,
            responseRate: null,
            avgResponseTimeMinutes: null,
            completionRate: null);

        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/professionals/{professionalId}/trust-metrics");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal("pending", body.GetProperty("verificationStatus").GetString());
        Assert.Equal(0, body.GetProperty("badges").GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("responseRate").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("completionRate").ValueKind);

        // The metrics-only endpoint must not leak the rest of the professional profile.
        Assert.False(body.TryGetProperty("user", out _));
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

        public async Task SeedProfessionalAsync(
            string id,
            string verificationStatus,
            string? badges,
            double? responseRate,
            int? avgResponseTimeMinutes,
            double? completionRate)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();

            var userId = $"user-{id}";
            ctx.Users.Add(new User(userId, $"Pro {id}", $"{id}@test.com", null, "PROFESSIONAL", null, DateTime.UtcNow));
            ctx.Professionals.Add(new Professional(
                Id: id,
                UserId: userId,
                Bio: null,
                Rating: 4.5,
                Active: true,
                AvatarUrl: null,
                AvailabilityText: null,
                CompletedJobsCount: 12,
                SlotMinutes: null,
                LeadTimeMinutes: null,
                MaxAdvanceDays: null,
                AllowInstantBooking: null,
                ResponseRate: responseRate,
                AvgResponseTimeMinutes: avgResponseTimeMinutes,
                CompletionRate: completionRate,
                VerificationStatus: verificationStatus,
                Badges: badges));

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
