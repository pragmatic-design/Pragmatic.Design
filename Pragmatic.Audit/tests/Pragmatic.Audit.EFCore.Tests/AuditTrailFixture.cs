using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Audit.EFCore.Tests;

/// <summary>
///     An audit trail over a private in-memory SQLite database, with a clock the test drives.
/// </summary>
/// <remarks>
///     The clock is controllable because sealing and retention are both defined in terms of elapsed
///     time. Waiting for real hours is not an option, and asserting on a real clock would make the
///     suite fail whenever the machine is slow — the kind of intermittent red that teaches people to
///     re-run instead of read.
/// </remarks>
internal sealed class AuditTrailFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public AuditTrailFixture(DateTimeOffset? start = null)
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        Db = new AuditDbContext(
            new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(_connection).Options);
        Db.Database.EnsureCreated();

        Clock = new FakeTimeProvider(start ?? new DateTimeOffset(2026, 7, 30, 14, 0, 0, TimeSpan.Zero));
        Naming = new HourlyAuditSegmentNaming();

        Trail = new EfCoreAuditTrail(Db, new PatternAuditDetailRedactor(), Naming, Clock);
        Reader = new EfCoreAuditTrailReader(Db);
        Sealing = new AuditSealingService(Db, Naming, Clock);
        Retention = new AuditRetentionService(Db, Clock);
    }

    /// <summary>The context's own connection — what an enlisting caller has to be transacting on.</summary>
    public SqliteConnection Connection => _connection;

    public AuditDbContext Db { get; }
    public FakeTimeProvider Clock { get; }
    public HourlyAuditSegmentNaming Naming { get; }
    public EfCoreAuditTrail Trail { get; }
    public EfCoreAuditTrailReader Reader { get; }
    public AuditSealingService Sealing { get; }
    public AuditRetentionService Retention { get; }

    public AuditEntry Entry(string operation = "Test.Operation", DateTimeOffset? at = null) => new()
    {
        SegmentId = string.Empty,          // assigned by the trail
        OccurredAt = at ?? Clock.GetUtcNow(),
        Category = AuditCategory.Data,
        Operation = operation,
        SubjectRef = "subject-1",
        Outcome = AuditOutcome.Success
    };

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }

    internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
