using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Domain.Enums;
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
/// HTTP-level coverage for PRD "Fluxo de leads: limite do cliente, priorização por reputação e
/// fechamento": (1) aceitar uma única proposta, mesmo antes do limite, fecha o lead imediatamente
/// e ele some da busca de leads para todo mundo; (2) atingir o limite do cliente transiciona o
/// pedido para 'propostas_completas' e uma proposta adicional é rejeitada com 422.
/// </summary>
public sealed class LeadFlowEndpointTests : IClassFixture<LeadFlowEndpointTests.ApiFactory>
{
    private readonly ApiFactory _factory;

    public LeadFlowEndpointTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AcceptingSingleProposal_ClosesLeadImmediately_EvenBelowLimit()
    {
        var orderId = await _factory.SeedLeadAsync(clientId: "client-a", serviceId: "svc-a", maxProposals: 5);
        using var client = _factory.CreateClient();

        var proposalId = await CreateAndSendProposalAsync(client, orderId, professionalId: "pro-1", clientId: "client-a", serviceId: "svc-a");

        var acceptResponse = await client.PostAsJsonAsync($"/proposals/{proposalId}/accept", new
        {
            ClientId = "client-a",
            PaymentMethod = "pix",
            UseDefaultAddress = false,
            ServiceAddress = new
            {
                ZipCode = "01310100",
                Street = "Av. Paulista",
                Number = "1000",
                Neighborhood = "Bela Vista",
                City = "São Paulo",
                State = "SP",
                Complement = (string?)null,
                Reference = (string?)null
            }
        });
        Assert.Equal(HttpStatusCode.Created, acceptResponse.StatusCode);

        var order = await _factory.GetOrderAsync(orderId);
        Assert.Equal(OrderStatus.Convertido, order!.Status);

        var listAfter = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/orders")
        {
            Headers = { { "Cache-Control", "no-cache" } }
        });
        var body = await listAfter.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.EnumerateArray().Select(o => o.GetProperty("id").GetString()).ToList();
        Assert.DoesNotContain(orderId, ids);
    }

    [Fact]
    public async Task ReachingMaxProposals_ClosesForNewProposals_With422()
    {
        var orderId = await _factory.SeedLeadAsync(clientId: "client-b", serviceId: "svc-b", maxProposals: 2);
        using var client = _factory.CreateClient();

        await CreateProposalAsync(client, orderId, professionalId: "pro-1", clientId: "client-b", serviceId: "svc-b");
        await CreateProposalAsync(client, orderId, professionalId: "pro-2", clientId: "client-b", serviceId: "svc-b");

        var order = await _factory.GetOrderAsync(orderId);
        Assert.Equal(OrderStatus.PropostasCompletas, order!.Status);

        var thirdResponse = await PostProposalRawAsync(client, orderId, professionalId: "pro-3", clientId: "client-b", serviceId: "svc-b");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, thirdResponse.StatusCode);
    }

    private static async Task<string> CreateAndSendProposalAsync(
        HttpClient client, string sourceOrderId, string professionalId, string clientId, string serviceId)
    {
        var proposalId = await CreateProposalAsync(client, sourceOrderId, professionalId, clientId, serviceId);
        var sendResponse = await client.PutAsJsonAsync($"/proposals/{proposalId}/send", new { ProfessionalId = professionalId });
        sendResponse.EnsureSuccessStatusCode();
        return proposalId;
    }

    private static async Task<string> CreateProposalAsync(
        HttpClient client, string sourceOrderId, string professionalId, string clientId, string serviceId)
    {
        var response = await PostProposalRawAsync(client, sourceOrderId, professionalId, clientId, serviceId);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> PostProposalRawAsync(
        HttpClient client, string sourceOrderId, string professionalId, string clientId, string serviceId)
        => client.PostAsJsonAsync("/proposals", new
        {
            ProfessionalId = professionalId,
            ClientId = clientId,
            ServiceId = serviceId,
            Scope = "Pintura completa",
            PriceTotalCents = 20000,
            ValidUntil = DateTime.UtcNow.AddDays(3).ToString("O"),
            ProfessionalServiceId = (string?)null,
            ConversationId = (string?)null,
            IncludesDescription = (string?)null,
            ExcludesDescription = (string?)null,
            PriceByStage = (string?)null,
            DurationEstimate = (string?)null,
            SuggestedDatetime = (string?)null,
            VisitFeeCents = 0,
            SourceOrderId = sourceOrderId
        });

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

        public async Task<string> SeedLeadAsync(string clientId, string serviceId, int maxProposals)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();

            if (!await ctx.Users.AnyAsync(u => u.Id == clientId))
                ctx.Users.Add(new User(clientId, "Client", $"{clientId}@test.com", null, "CLIENT", null, DateTime.UtcNow));
            if (!await ctx.Services.AnyAsync(s => s.Id == serviceId))
                ctx.Services.Add(new Service(serviceId, "Pintura", null, DateTime.UtcNow));

            var order = Order.Create(
                id: Guid.NewGuid().ToString(),
                clientId: clientId,
                serviceId: serviceId,
                description: null,
                location: null,
                date: null,
                maxProposals: maxProposals);
            ctx.Orders.Add(order);

            await ctx.SaveChangesAsync();
            return order.Id;
        }

        public async Task<Order?> GetOrderAsync(string orderId)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await ctx.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
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
