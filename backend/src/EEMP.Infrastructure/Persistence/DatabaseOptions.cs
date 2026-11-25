namespace EEMP.Infrastructure.Persistence;

public enum DatabaseProvider
{
    SqlServer,
    /// <summary>Test/local fallback only. Schema is created from the model (no migrations); rowversion is emulated.</summary>
    Sqlite,
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;
    public string ConnectionString { get; set; } = string.Empty;
    public bool ApplyMigrationsOnStartup { get; set; }
}
