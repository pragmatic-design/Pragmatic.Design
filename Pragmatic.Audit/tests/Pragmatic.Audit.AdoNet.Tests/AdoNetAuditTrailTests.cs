using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Audit.EFCore;

namespace Pragmatic.Audit.AdoNet.Tests;

/// <summary>
///     The ADO.NET writer, and above all its agreement with the EF one.
/// </summary>
/// <remarks>
///     Two write paths into one trail is a standing invitation to divergence: a column named slightly
///     differently, a timestamp stored as text rather than ticks, a category written as a string. None
///     of that fails at write time — it fails much later, when a reader returns nothing and nobody can
///     say why. So the central test writes with one path and reads with the other.
/// </remarks>
public class AdoNetAuditTrailTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 15, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly SqliteConnection _connection;
    private readonly AuditDbContext _db;
    private readonly AdoNetAuditTrail _trail;

    public AdoNetAuditTrailTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _db = new AuditDbContext(
            new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _trail = new AdoNetAuditTrail(
            new SqliteAuditDialect(),
            new AuditEntryPreparer(new PatternAuditDetailRedactor(), new HourlyAuditSegmentNaming(), new FixedClock()));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static AuditEntry Entry(string operation = "Config.Changed") => new()
    {
        SegmentId = string.Empty,
        Operation = operation,
        Category = AuditCategory.Configuration,
        Outcome = AuditOutcome.Success,
        TargetType = "config",
        TargetId = "App:Timeout",
        TenantId = "acme",
    };

    [Fact]
    public async Task WhatTheAdoNetWriterWrites_TheEfReaderReadsBack()
    {
        // The property that matters. Every column, and the tick encoding of the timestamp, has to agree
        // between the two paths — and a mismatch is silent until someone queries and finds nothing.
        await _trail.RecordAsync(Entry(), _connection);

        _db.ChangeTracker.Clear();
        var stored = await _db.Entries.SingleAsync();

        stored.Operation.Should().Be("Config.Changed");
        stored.Category.Should().Be(AuditCategory.Configuration);
        stored.Outcome.Should().Be(AuditOutcome.Success);
        stored.TargetType.Should().Be("config");
        stored.TargetId.Should().Be("App:Timeout");
        stored.TenantId.Should().Be("acme");
        stored.OccurredAt.Should().Be(Now);
    }

    [Fact]
    public async Task TheFirstWriteOfASegment_OpensIt()
    {
        await _trail.RecordAsync(Entry(), _connection);

        _db.ChangeTracker.Clear();
        var segment = await _db.Segments.SingleAsync();

        segment.SegmentId.Should().Be(new HourlyAuditSegmentNaming().SegmentFor(Now));
        segment.OpenedAt.Should().Be(Now);
        segment.IsSealed.Should().BeFalse();
    }

    [Fact]
    public async Task ASecondWriteToTheSameSegment_DoesNotOpenItTwice()
    {
        await _trail.RecordAsync(Entry("First"), _connection);
        await _trail.RecordAsync(Entry("Second"), _connection);

        _db.ChangeTracker.Clear();
        (await _db.Segments.CountAsync()).Should().Be(1);
        (await _db.Entries.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task AnEntryWrittenInARolledBackTransaction_DoesNotSurvive()
    {
        // The whole reason this writer exists: the producer's change and its audit row share a fate.
        var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync();
        await using (transaction.ConfigureAwait(false))
        {
            await _trail.RecordAsync(Entry(), _connection, transaction);
            await transaction.RollbackAsync();
        }

        _db.ChangeTracker.Clear();
        (await _db.Entries.CountAsync()).Should().Be(0);
        (await _db.Segments.CountAsync()).Should().Be(0, "the segment it opened must go too");
    }

    [Fact]
    public async Task AnEntryWrittenInACommittedTransaction_Survives()
    {
        var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync();
        await using (transaction.ConfigureAwait(false))
        {
            await _trail.RecordAsync(Entry(), _connection, transaction);
            await transaction.CommitAsync();
        }

        _db.ChangeTracker.Clear();
        (await _db.Entries.SingleAsync()).Operation.Should().Be("Config.Changed");
    }

    [Fact]
    public async Task AppendingToASealedSegment_IsRefused()
    {
        await _trail.RecordAsync(Entry("First"), _connection);

        _db.ChangeTracker.Clear();
        var segment = await _db.Segments.SingleAsync();
        segment.SealedAt = Now.AddMinutes(1);
        segment.MerkleRoot = [1, 2, 3];
        segment.SegmentHash = [4, 5, 6];
        await _db.SaveChangesAsync();

        var act = async () => await _trail.RecordAsync(Entry("Late"), _connection);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*already sealed*");

        _db.ChangeTracker.Clear();
        (await _db.Entries.CountAsync()).Should().Be(1, "the refusal must not have written anything");
    }

    [Fact]
    public async Task TheDetail_IsRedactedOnThisPathToo()
    {
        // Shared shaping is the point of AuditEntryPreparer; this is what proves this writer uses it.
        var entry = Entry();
        entry.Detail = "changed by ada@example.com";

        await _trail.RecordAsync(entry, _connection);

        _db.ChangeTracker.Clear();
        var stored = await _db.Entries.SingleAsync();
        stored.Detail.Should().NotContain("ada@example.com");
        stored.Detail.Should().Contain("[redacted]");
    }

    [Fact]
    public async Task TheDialectsOwnSchema_IsOneTheEfModelAgreesWith()
    {
        // The DDL in the dialect is hand-written; the EF configuration is the model everything else
        // reads through. Nothing checks that they match — so this does, by building the schema from the
        // dialect alone and then reading what was written through EF. A column named differently, or a
        // timestamp typed as text, fails right here instead of in production.
        var connection = new SqliteConnection("Filename=:memory:");
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync();

            var create = connection.CreateCommand();
            await using (create.ConfigureAwait(false))
            {
                create.CommandText = new SqliteAuditDialect().CreateSchema;
                await create.ExecuteNonQueryAsync();
            }

            await _trail.RecordAsync(Entry("Schema.Check"), connection);

            var db = new AuditDbContext(
                new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connection).Options);
            await using (db.ConfigureAwait(false))
            {
                var stored = await db.Entries.SingleAsync();
                stored.Operation.Should().Be("Schema.Check");
                stored.OccurredAt.Should().Be(Now);
                stored.Category.Should().Be(AuditCategory.Configuration);

                (await db.Segments.CountAsync()).Should().Be(1);
            }
        }
    }

    [Fact]
    public async Task AnEntryWithoutAnOperation_IsRefusedBeforeAnySql()
    {
        var act = async () => await _trail.RecordAsync(Entry(operation: " "), _connection);

        await act.Should().ThrowAsync<ArgumentException>();
        (await _db.Segments.CountAsync()).Should().Be(0, "a rejected entry must not have opened a segment");
    }
}
