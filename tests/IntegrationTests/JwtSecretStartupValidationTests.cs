using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Covers PRD-Correcao-Testes-Autorizacao-Anexo Sec. 2: a JWT_SECRET shorter than 256 bits
/// (32 bytes) must fail application startup instead of surfacing as a cryptic IDX10720 error
/// on the first authenticated request.
/// </summary>
public sealed class JwtSecretStartupValidationTests
{
    [Fact]
    public void Application_Fails_Fast_When_JwtSecret_Is_Shorter_Than_32_Bytes()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", "too-short-secret");
        Environment.SetEnvironmentVariable("STORAGE_BUCKET_NAME", "test-bucket");
        Environment.SetEnvironmentVariable("CORS_ALLOWED_ORIGINS", "http://localhost");

        using var factory = new WebApplicationFactory<Program>();

        Assert.Throws<InvalidOperationException>(() => factory.Services);
    }
}
