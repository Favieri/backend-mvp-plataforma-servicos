namespace Infrastructure.Data;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
    public int CommandTimeoutSeconds { get; set; } = 15;
    // Default 5: designed for Lambda + Supabase transaction pooler (port 6543).
    // Override with DB_MAX_POOL_SIZE env var for non-serverless deployments.
    public int MaximumPoolSize { get; set; } = 5;
    public int PoolerPort { get; set; } = 6543;
}
