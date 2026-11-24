using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace EEMP.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in-process on the SQLite fallback provider (no SQL Server/Docker in CI sandbox).
/// Each factory gets its own database file, the development seed and test-only secrets.
/// </summary>
public class EempFactory : WebApplicationFactory<Program>
{
    public const string PlatformPassword = "PlatformTest1";
    public const string AdminPassword = "AdminTest1";
    public const string UserPassword = "UserTest1";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"eemp-it-{Guid.NewGuid():N}.db");

    protected virtual int AuthPermitLimit => 100_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Seed:PlatformAdminPassword", PlatformPassword);
        builder.UseSetting("Seed:DemoAdminPassword", AdminPassword);
        builder.UseSetting("Seed:DemoUserPassword", UserPassword);
        builder.UseSetting("RateLimiting:AuthPermitLimit", AuthPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:AuthWindowSeconds", "60");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }
}

public sealed class RateLimitedFactory : EempFactory
{
    protected override int AuthPermitLimit => 3;
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<EempFactory>
{
    public const string Name = "api";
}
