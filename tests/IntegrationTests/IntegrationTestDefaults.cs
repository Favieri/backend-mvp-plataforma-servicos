namespace IntegrationTests;

/// <summary>
/// Valores e configuração compartilhados por todos os WebApplicationFactory de
/// teste de integração. Existe para que um valor como o segredo JWT de teste
/// seja definido em um único lugar — evita a duplicação que já causou o mesmo
/// bug (segredo curto demais para HS256) em múltiplos arquivos de teste.
/// </summary>
public static class IntegrationTestDefaults
{
    /// <summary>
    /// 52 bytes — folgado acima do mínimo de 32 exigido pelo HS256, para que
    /// uma futura edição acidental não volte a encurtar o suficiente para
    /// quebrar a validação de boot.
    /// </summary>
    public const string JwtSecret = "test-jwt-secret-for-integration-tests-only-32bytes";

    public static void ConfigureTestEnvironment()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", JwtSecret);
        Environment.SetEnvironmentVariable("STORAGE_BUCKET_NAME", "test-bucket");
        Environment.SetEnvironmentVariable("CORS_ALLOWED_ORIGINS", "http://localhost");
    }
}
