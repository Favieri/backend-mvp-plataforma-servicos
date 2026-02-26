namespace Infrastructure.Data;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public string ConnectionString { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
}
