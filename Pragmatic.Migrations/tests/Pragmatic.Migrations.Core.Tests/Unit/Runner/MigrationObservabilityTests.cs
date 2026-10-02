#pragma warning disable CA2007 // ConfigureAwait in test code

using System.Data.Common;
using System.Diagnostics.Metrics;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Maintenance;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Core.Tests.Provider;

namespace Pragmatic.Migrations.Core.Tests.Unit.Runner;

/// <summary>
///     What the runner reports about itself: seed providers, the progress stream, log records and
///     OpenTelemetry metrics. The metrics in particular were declared and documented but never
///     emitted — the meter exposed nothing at all — so each instrument is asserted here against a
///     real <see cref="MeterListener"/>.
/// </summary>
public class MigrationObservabilityTests
{
    private static SchemaVersion Desired(string databaseName = "obs") => new(
        [
            new TableSchema("Widgets", null,
                [new ColumnSchema("Id", "TEXT", false, true)],
                [], [])
        ],
        DatabaseName: databaseName,
        ProviderName: MigrationConstants.ProviderSqlite);

    private static async Task<MigrationResult> MigrateAsync(
        SqliteConnection conn,
        Action<MigrationsBuilder>? configure = null,
        Action<IServiceCollection>? register = null,
        SchemaVersion? desired = null)
    {
        var services = new ServiceCollection();
        var builder = new MigrationsBuilder(services);
        builder.UseProvider(MigrationConstants.ProviderSqlite, _ => new NonOwningConnection(conn));
        configure?.Invoke(builder);
        builder.Build();
        register?.Invoke(services);

        await using var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<IMigrationRunner>().MigrateAsync(
            new MigrationContext(conn.ConnectionString, desired ?? Desired(),
                provider.GetRequiredService<MigrationOptions>()));
    }

    // =====================================================================================
    // Seed providers
    // =====================================================================================

    [Fact]
    public async Task SeedProvider_RunsAfterTheSchemaChanges()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var seed = new RecordingSeed();
        var result = await MigrateAsync(conn, register: s => s.AddSingleton<IMigrationSeedProvider>(seed));

        result.Success.Should().BeTrue(result.Error);
        seed.Invocations.Should().Be(1);
        seed.TableExistedWhenSeeding.Should().BeTrue("seeding runs once the schema is in place");
    }

    [Fact]
    public async Task SeedProvider_DoesNotRunWhenNothingChanged()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        await MigrateAsync(conn);   // bring the schema up to date first

        var seed = new RecordingSeed();
        await MigrateAsync(conn, register: s => s.AddSingleton<IMigrationSeedProvider>(seed));

        seed.Invocations.Should().Be(0, "seeding is tied to changes actually being applied");
    }

    [Fact]
    public async Task SeedProvider_ForAnotherDatabase_IsSkipped()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var seed = new RecordingSeed { DatabaseName = "some-other-db" };
        await MigrateAsync(conn, register: s => s.AddSingleton<IMigrationSeedProvider>(seed));

        seed.Invocations.Should().Be(0);
    }

    // =====================================================================================
    // Progress stream
    // =====================================================================================

    [Fact]
    public async Task ProgressStream_ReportsThePhasesOfARun()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var progress = new RecordingProgressStream();
        var result = await MigrateAsync(conn, register: s => s.AddSingleton<IMigrationProgressStream>(progress));

        result.Success.Should().BeTrue(result.Error);
        progress.Events.Select(e => e.Phase).Should().Contain(["analyzing", "applying", "complete"]);
        progress.Events.Should().AllSatisfy(e => e.DatabaseName.Should().Be("obs"));
        progress.Events.Last().ProgressPercent.Should().Be(100.0);
        progress.Events.Should().NotContain(e => e.IsError);
    }

    [Fact]
    public async Task ProgressStream_ReportsTheFailureWhenBreakingChangesAreBlocked()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        await MigrateAsync(conn);

        // Dropping the only column of the table is breaking, so the run is refused.
        var breaking = new SchemaVersion(
            [new TableSchema("Widgets", null, [new ColumnSchema("Other", "TEXT", true, false)], [], [])],
            DatabaseName: "obs", ProviderName: MigrationConstants.ProviderSqlite);

        var progress = new RecordingProgressStream();
        var result = await MigrateAsync(conn, register: s => s.AddSingleton<IMigrationProgressStream>(progress),
            desired: breaking);

        result.Success.Should().BeFalse();
        progress.Events.Should().Contain(e => e.IsError && e.Phase == "error");
    }

    // =====================================================================================
    // Metrics
    // =====================================================================================

    [Fact]
    public async Task Metrics_AreEmittedForASuccessfulRun()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        const string db = "obs-metrics-success";
        using var recorder = new MetricRecorder(db);
        var result = await MigrateAsync(conn, desired: Desired(db));

        result.Success.Should().BeTrue(result.Error);

        recorder.Sum("pragmatic.migrations.executed").Should().Be(1);
        recorder.Sum("pragmatic.migrations.failed").Should().Be(0);
        recorder.Sum("pragmatic.migrations.changes_applied").Should().BeGreaterThan(0);
        recorder.Count("pragmatic.migrations.duration").Should().Be(1);

        recorder.TagsOf("pragmatic.migrations.executed")
            .Should().Contain(t => t.Key == "db.name" && (string?)t.Value == db)
            .And.Contain(t => t.Key == "db.provider" && (string?)t.Value == MigrationConstants.ProviderSqlite);
    }

    [Fact]
    public async Task Metrics_CountAFailedRunAsFailed()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        const string db = "obs-metrics-failure";
        await MigrateAsync(conn, desired: Desired(db));

        var breaking = new SchemaVersion(
            [new TableSchema("Widgets", null, [new ColumnSchema("Other", "TEXT", true, false)], [], [])],
            DatabaseName: db, ProviderName: MigrationConstants.ProviderSqlite);

        using var recorder = new MetricRecorder(db);
        var result = await MigrateAsync(conn, desired: breaking);

        result.Success.Should().BeFalse();
        recorder.Sum("pragmatic.migrations.executed").Should().Be(1);
        recorder.Sum("pragmatic.migrations.failed").Should().Be(1);
        recorder.Sum("pragmatic.migrations.changes_applied").Should().Be(0);
    }

    // =====================================================================================
    // Logging
    // =====================================================================================

    [Fact]
    public async Task Logging_RecordsTheLifecycleOfARun()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var logger = new RecordingLogger();
        await MigrateAsync(conn, register: s => s.AddSingleton<ILogger<MigrationRunner>>(logger));

        logger.Messages.Should().Contain(m => m.Contains("Starting migration", StringComparison.Ordinal));
        logger.Messages.Should().Contain(m => m.Contains("Diff computed", StringComparison.Ordinal));
        logger.Messages.Should().Contain(m => m.Contains("Migration complete", StringComparison.Ordinal));
        logger.Messages.Should().AllSatisfy(m => m.Should().Contain("[obs]"));
    }

    [Fact]
    public async Task Logging_WarnsWhenBreakingChangesAreBlocked()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        await MigrateAsync(conn);

        var breaking = new SchemaVersion(
            [new TableSchema("Widgets", null, [new ColumnSchema("Other", "TEXT", true, false)], [], [])],
            DatabaseName: "obs", ProviderName: MigrationConstants.ProviderSqlite);

        var logger = new RecordingLogger();
        await MigrateAsync(conn, register: s => s.AddSingleton<ILogger<MigrationRunner>>(logger), desired: breaking);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("Blocked", StringComparison.Ordinal));
    }

    // =====================================================================================
    // Test doubles
    // =====================================================================================

    private sealed class RecordingSeed : IMigrationSeedProvider
    {
        public string? DatabaseName { get; init; }
        public int Order => 0;
        public int Invocations { get; private set; }
        public bool TableExistedWhenSeeding { get; private set; }

        public async Task SeedAsync(DbConnection connection, CancellationToken ct = default)
        {
            Invocations++;
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Widgets'";
            TableExistedWhenSeeding = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) == 1;
        }
    }

    private sealed class RecordingProgressStream : IMigrationProgressStream
    {
        public List<MigrationProgressEvent> Events { get; } = [];

        public void Report(MigrationProgressEvent progressEvent) => Events.Add(progressEvent);

        public void Complete() { }

        // The consumer side of the stream is not what these tests observe; they read Events.
        public async IAsyncEnumerable<MigrationProgressEvent> StreamAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            foreach (var e in Events)
            {
                ct.ThrowIfCancellationRequested();
                yield return e;
            }
        }
    }

    /// <summary>Captures every measurement published by the Pragmatic.Migrations meter.</summary>
    /// <summary>
    ///     Captures the measurements published for ONE database. A MeterListener is process-wide
    ///     and xUnit runs test classes in parallel, so without the filter another test's migration
    ///     would be counted here.
    /// </summary>
    private sealed class MetricRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Lock _gate = new();
        private readonly List<(string Instrument, double Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];
        private readonly string _databaseName = "obs";

        private MetricRecorder() { }

        public MetricRecorder(string databaseName) : this()
        {
            _databaseName = databaseName;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Pragmatic.Migrations")
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i.Name, v, tags));
            _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i.Name, v, tags));
            _listener.Start();
        }

        private void Record(string instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copy = tags.ToArray();
            if (!copy.Any(t => t.Key == "db.name" && (string?)t.Value == _databaseName))
                return;

            lock (_gate) _measurements.Add((instrument, value, copy));
        }

        public double Sum(string instrument)
        {
            lock (_gate) return _measurements.Where(m => m.Instrument == instrument).Sum(m => m.Value);
        }

        public int Count(string instrument)
        {
            lock (_gate) return _measurements.Count(m => m.Instrument == instrument);
        }

        public IEnumerable<KeyValuePair<string, object?>> TagsOf(string instrument)
        {
            lock (_gate) return _measurements.Where(m => m.Instrument == instrument).SelectMany(m => m.Tags).ToList();
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class RecordingLogger : ILogger<MigrationRunner>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IEnumerable<string> Messages => Entries.Select(e => e.Message);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
