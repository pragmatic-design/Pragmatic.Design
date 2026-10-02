using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Audit.EFCore.Tests;

/// <summary>
///     <c>AddAuditTrail</c> seals the trail for as long as the host runs: verification checks sealed
///     segments only, and nothing else seals them.
/// </summary>
/// <remarks>
///     <see cref="AuditSealingService" /> was called by tests alone. In an application no segment was
///     ever sealed, so <c>VerifyAsync</c> checked none and reported the trail intact — altered or not.
///     The tick is asserted directly; the timer around it is the host's.
/// </remarks>
public sealed class TheTrailIsSealedWhileTheHostRunsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly AuditTrailFixture.FakeTimeProvider _clock = new(new DateTimeOffset(2026, 7, 30, 14, 0, 0, TimeSpan.Zero));
    private readonly CapturedLogs _logs = new();
    private readonly ServiceProvider _services;

    public TheTrailIsSealedWhileTheHostRunsTests()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddLogging(logging => logging.AddProvider(_logs));
        services.AddDbContext<AuditDbContext>(options => options.UseSqlite(_connection));
        services.AddAuditTrail();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.EnsureCreated();
    }

    private AuditSealingWorker Worker => _services.GetServices<IHostedService>().OfType<AuditSealingWorker>().Single();

    private static TimeSpan PastTheWindowAndItsGrace => TimeSpan.FromHours(1) + new HourlyAuditSegmentNaming().GracePeriod + TimeSpan.FromMinutes(1);

    [Fact]
    public void AddAuditTrail_RunsTheSealerWithTheHost()
    {
        _services.GetServices<IHostedService>().OfType<AuditSealingWorker>().Should().ContainSingle();
    }

    [Fact]
    public async Task ATick_SealsWhatIsDue_SoVerificationHasSomethingToCheck()
    {
        await RecordAsync("A");
        _clock.Advance(PastTheWindowAndItsGrace);

        var before = await VerifyAsync();
        var sealedIds = await Worker.TickAsync(CancellationToken.None);
        var after = await VerifyAsync();

        // What a trail nobody seals reports: intact, having checked nothing.
        before.SegmentsChecked.Should().Be(0);
        before.IsIntact.Should().BeTrue();

        sealedIds.Should().ContainSingle();
        after.SegmentsChecked.Should().Be(1);
        after.IsIntact.Should().BeTrue();
    }

    /// <summary>The control: a segment inside its grace period is left open.</summary>
    [Fact]
    public async Task ATick_InsideTheGracePeriod_SealsNothing()
    {
        await RecordAsync("A");
        _clock.Advance(TimeSpan.FromHours(1));

        var sealedIds = await Worker.TickAsync(CancellationToken.None);

        sealedIds.Should().BeEmpty();
    }

    /// <summary>
    ///     A failed pass is reported and returns: the segments stay due for the next tick, and an
    ///     exception escaping would end the host's only sealer.
    /// </summary>
    [Fact]
    public async Task ATickThatFails_IsReportedAndDoesNotEndTheSealer()
    {
        // The context reopens a closed connection itself, onto a new and empty in-memory database.
        _connection.Close();

        var sealedIds = await Worker.TickAsync(CancellationToken.None);

        sealedIds.Should().BeEmpty();
        _logs.Entries.Should().Contain(e =>
            e.Category == typeof(AuditSealingWorker).FullName && e.Level == LogLevel.Warning);
    }

    private async Task RecordAsync(string operation)
    {
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditTrail>().RecordAsync(new AuditEntry
        {
            SegmentId = string.Empty,
            OccurredAt = _clock.GetUtcNow(),
            Category = AuditCategory.Data,
            Operation = operation,
            Outcome = AuditOutcome.Success
        });
    }

    private async Task<IntegrityReport> VerifyAsync()
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAuditTrailReader>()
            .VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public List<(string Category, LogLevel Level)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, List<(string Category, LogLevel Level)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => entries.Add((category, logLevel));
        }
    }
}
