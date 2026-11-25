using EEMP.Api.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EEMP.UnitTests;

/// <summary>
/// Regression: the API started outside its output directory (content root without appsettings.Development.json)
/// skipped the development seed silently although the Seed__*Password variables were set, so every seeded
/// sign-in returned 401 in the e2e-real-api CI job. The skip must now be logged as a warning.
/// </summary>
public class DevelopmentSeedTests
{
    [Fact]
    public async Task Disabled_seed_with_passwords_configured_logs_a_warning()
    {
        var (provider, sink) = Build(new()
        {
            ["Seed:PlatformAdminPassword"] = "Platform123",
            ["Seed:DemoAdminPassword"] = "DemoAdmin123",
            ["Seed:DemoUserPassword"] = "DemoUser123",
        });

        await provider.RunDevelopmentSeedAsync();

        var warning = Assert.Single(sink.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("Seed:Enabled is false", warning.Message);
    }

    [Fact]
    public async Task Disabled_seed_without_passwords_stays_quiet()
    {
        var (provider, sink) = Build(new());

        await provider.RunDevelopmentSeedAsync();

        Assert.DoesNotContain(sink.Entries, e => e.Level >= LogLevel.Warning);
    }

    private static (ServiceProvider, CapturingLoggerProvider) Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var sink = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(sink));
        services.AddDevelopmentSeed(configuration);
        return (services.BuildServiceProvider(), sink);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Logger(Entries);
        public void Dispose() { }

        private sealed class Logger(List<(LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (entries) entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
