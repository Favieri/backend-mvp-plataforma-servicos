namespace Infrastructure.Data;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
    public int CommandTimeoutSeconds { get; set; } = 15;
    public int MaximumPoolSize { get; set; } = 30;
    public int PoolerPort { get; set; } = 6543;
}
