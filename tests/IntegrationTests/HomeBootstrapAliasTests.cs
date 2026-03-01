using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace IntegrationTests;

public sealed class HomeBootstrapAliasTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HomeBootstrapAliasTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HomeBootstrap_ShouldRedirectToBootstrap()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/home/bootstrap");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/bootstrap", response.Headers.Location?.ToString());
    }
}
