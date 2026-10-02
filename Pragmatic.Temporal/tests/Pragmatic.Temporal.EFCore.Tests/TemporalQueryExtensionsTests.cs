using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Temporal.EntityFrameworkCore.QueryExtensions;
using Pragmatic.Temporal.Testing;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>
///     Boundary semantics of the temporal query extensions, executed against real
///     Sqlite SQL (any client evaluation would throw at runtime).
/// </summary>
public class TemporalQueryExtensionsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<QueryContext> _options;

    private static DateTimeOffset Utc(int month, int day, int hour = 0)
        => new(2026, month, day, hour, 0, 0, TimeSpan.Zero);

    public TemporalQueryExtensionsTests()
    {
        (_connection, _options) = SqliteContextFactory.CreateOptions<QueryContext>();

        using var context = new QueryContext(_options);
        context.Database.EnsureCreated();
        context.Events.AddRange(
            new QueryEvent { Id = 1, Timestamp = Utc(6, 1) },
            new QueryEvent { Id = 2, Timestamp = Utc(6, 10, 12) },
            new QueryEvent { Id = 3, Timestamp = Utc(6, 15) },   // boundary
            new QueryEvent { Id = 4, Timestamp = Utc(7, 1) },
            new QueryEvent { Id = 5, Timestamp = Utc(12, 31, 23) });
        context.SaveChanges();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public void WhereBetween_EndIsExclusive()
    {
        using var context = new QueryContext(_options);

        var ids = context.Events
            .WhereBetween(e => e.Timestamp, Utc(6, 1), Utc(6, 15))
            .Select(e => e.Id)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal([1, 2], ids); // id 3 sits exactly on end → excluded
    }

    [Fact]
    public void AsOf_IsInclusive()
    {
        using var context = new QueryContext(_options);

        var ids = context.Events
            .AsOf(e => e.Timestamp, Utc(6, 15))
            .Select(e => e.Id)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal([1, 2, 3], ids); // id 3 exactly at the moment → included
    }

    [Fact]
    public void WhereBetweenDates_ToIsInclusive()
    {
        using var context = new QueryContext(_options);

        var ids = context.Events
            .WhereBetweenDates(
                e => e.Timestamp,
                new LocalDate(2026, 6, 1),
                new LocalDate(2026, 6, 15),
                TimeZoneInfo.Utc)
            .Select(e => e.Id)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal([1, 2, 3], ids); // whole day of June 15 included
    }

    [Fact]
    public void WhereQuarter_ValidatesRange()
    {
        using var context = new QueryContext(_options);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            context.Events.WhereQuarter(e => e.Timestamp, 2026, 5, TimeZoneInfo.Utc));
    }

    [Fact]
    public void WhereQuarter_FiltersByQuarter()
    {
        using var context = new QueryContext(_options);

        var ids = context.Events
            .WhereQuarter(e => e.Timestamp, 2026, 2, TimeZoneInfo.Utc)
            .Select(e => e.Id)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal([1, 2, 3], ids); // Q2 = Apr-Jun
    }

    [Fact]
    public void WhereLast_IncludesTodayAndCountsBack()
    {
        using var context = new QueryContext(_options);

        // Business today = 2026-06-15 (UTC test context)
        var temporalContext = TestTemporalContext.Utc(Utc(6, 15, 18));

        var ids = context.Events
            .WhereLast(e => e.Timestamp, 6, temporalContext)
            .Select(e => e.Id)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal([2, 3], ids); // window [Jun 10, Jun 16): id 2 and id 3, not id 1
    }
}
