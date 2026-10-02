using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Sequences;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Sequences;

/// <summary>
///     [GeneratedValue] {SEQ:N} is backed by a real database sequence (concurrency-safe, survives
///     restarts) rather than a static in-process counter. Verified here on SQLite (the counter-table
///     fallback); PostgreSQL is exercised end-to-end by the Showcase.
/// </summary>
public class SequenceValueProviderTests
{
    private sealed class ProbeContext(DbContextOptions options) : DbContext(options);

    [Fact]
    public async Task NextAsync_ReturnsSequentialValues()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using var db = new ProbeContext(options);

        var v1 = await SequenceValueProvider.NextAsync(db, "invoice_seq");
        var v2 = await SequenceValueProvider.NextAsync(db, "invoice_seq");
        var v3 = await SequenceValueProvider.NextAsync(db, "invoice_seq");

        v1.Should().Be(1);
        v2.Should().Be(2);
        v3.Should().Be(3);
    }

    [Fact]
    public async Task NextAsync_SeparateSequences_AreIndependent()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using var db = new ProbeContext(options);

        var a1 = await SequenceValueProvider.NextAsync(db, "a_seq");
        var a2 = await SequenceValueProvider.NextAsync(db, "a_seq");
        // A different sequence starts independently at 1.
        var b1 = await SequenceValueProvider.NextAsync(db, "b_seq");
        var a3 = await SequenceValueProvider.NextAsync(db, "a_seq");

        a1.Should().Be(1);
        a2.Should().Be(2);
        b1.Should().Be(1);
        a3.Should().Be(3);
    }
}
