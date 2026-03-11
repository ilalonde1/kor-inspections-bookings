using Kor.Inspections.App.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kor.Inspections.Tests.Services;

public class DeltekConfigValidationTests
{
    [Fact]
    public void ValidateDeltekConfiguration_AllSqlTemplatesPresent_DoesNotThrow()
    {
        var config = CreateConfig(
            "SELECT * FROM PR WHERE WBS1 = ?",
            "SELECT TOP 20 PR.WBS1 FROM PR WHERE PR.WBS1 LIKE ?");
        var logger = new ListLogger();

        DeltekConfigurationValidator.Validate(config, logger, strict: false);

        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void ValidateDeltekConfiguration_MissingSqlTemplate_StrictMode_Throws()
    {
        var config = CreateConfig(
            "",
            "SELECT TOP 20 PR.WBS1 FROM PR WHERE PR.WBS1 LIKE ?");
        var logger = new ListLogger();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeltekConfigurationValidator.Validate(config, logger, strict: true));

        Assert.Contains("Deltek:Sql_ProjectByNumber", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateDeltekConfiguration_MissingSqlTemplate_DevMode_LogsWarning()
    {
        var config = CreateConfig(
            "SELECT * FROM PR WHERE WBS1 = ?",
            "");
        var logger = new ListLogger();

        DeltekConfigurationValidator.Validate(config, logger, strict: false);

        var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("Deltek:Sql_ProjectSearchByPrefix", warning.Message, StringComparison.Ordinal);
    }

    private static IConfiguration CreateConfig(string projectByNumberSql, string searchSql)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Deltek:Sql_ProjectByNumber"] = projectByNumberSql,
                ["Deltek:Sql_ProjectSearchByPrefix"] = searchSql
            })
            .Build();
    }

    private sealed class ListLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public sealed record LogEntry(LogLevel Level, string Message);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
