using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ShouldReturnOk()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Health_ShouldGenerateCorrelationIdHeader_WhenMissing()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.True(response.Headers.TryGetValues("x-correlation-id", out var values));
        Assert.False(string.IsNullOrWhiteSpace(values.Single()));
    }

    [Fact]
    public async Task Health_ShouldEchoCorrelationIdHeader_WhenProvided()
    {
        const string expectedCorrelationId = "frontend-correlation-id";
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("x-correlation-id", expectedCorrelationId);

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues("x-correlation-id", out var values));
        Assert.Equal(expectedCorrelationId, values.Single());
    }
}
