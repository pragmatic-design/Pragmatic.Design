using Microsoft.EntityFrameworkCore;
using Pragmatic.Temporal.EntityFrameworkCore;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>
///     What reaches the column is UTC.
/// </summary>
/// <remarks>
///     <para>
///         The serialization side has always assumed it — <c>TemporalJsonModifier</c> treats an
///         unspecified <c>DateTimeKind</c> as UTC on the way out and says so in a comment calling it
///         the golden rule. Nothing made it true. An entity written with a non-zero offset reached the
///         column as it was, and every later read applied the outbound conversion on top of a value
///         that was already wrong.
///     </para>
///     <para>
///         ⚠️ The module's own round-trip tests could not see this: they read back what they wrote, and
///         a consistently wrong offset round-trips perfectly. The assertion has to be about the stored
///         value, not about the value coming back.
///     </para>
/// </remarks>
public class StorageIsUtcTests
{
    /// <summary>An instant written with an offset is stored as the same moment in UTC.</summary>
    [Fact]
    public async Task AnOffsetInstant_ReachesTheColumnAsUtc()
    {
        var (connection, options) = SqliteContextFactory.CreateOptions<InstantContext>(
            b => b.UsePragmaticTemporal());
        using var _ = connection;

        // 14:00 at +02:00 is 12:00 UTC. Same moment, and only one of the two spellings is stored.
        var written = new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.FromHours(2));

        using (var db = new InstantContext(options))
        {
            await db.Database.EnsureCreatedAsync().ConfigureAwait(true);
            db.Entities.Add(new InstantEntity { Moment = written, NullableMoment = written });
            await db.SaveChangesAsync().ConfigureAwait(true);
        }

        using (var db = new InstantContext(options))
        {
            var row = await db.Entities.SingleAsync().ConfigureAwait(true);

            row.Moment.Offset.Should().Be(TimeSpan.Zero, "the column holds UTC, not the caller's offset");
            row.Moment.Should().Be(written.ToUniversalTime(), "and it is the same instant");
            row.NullableMoment!.Value.Offset.Should().Be(TimeSpan.Zero);
        }
    }

    /// <summary>
    ///     A local <see cref="DateTime" /> is converted; an unspecified one is taken at its word.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The two are not the same operation, and treating them alike would move data.
    ///     <c>ToUniversalTime()</c> on an <c>Unspecified</c> value reads it as local and shifts it by
    ///     the machine's offset — so an instant that was already UTC would be silently moved on every
    ///     machine but one. Unspecified is assumed to be UTC instead, which is the rule the serializer
    ///     applies on the other side.
    /// </remarks>
    [Fact]
    public async Task AnUnspecifiedStamp_IsTakenAsUtc_AndALocalOneIsConverted()
    {
        var (connection, options) = SqliteContextFactory.CreateOptions<InstantContext>(
            b => b.UsePragmaticTemporal());
        using var _ = connection;

        var unspecified = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var local = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Local);

        using (var db = new InstantContext(options))
        {
            await db.Database.EnsureCreatedAsync().ConfigureAwait(true);
            db.Entities.Add(new InstantEntity { Stamp = unspecified, NullableStamp = local });
            await db.SaveChangesAsync().ConfigureAwait(true);
        }

        using (var db = new InstantContext(options))
        {
            var row = await db.Entities.SingleAsync().ConfigureAwait(true);

            row.Stamp.Kind.Should().Be(DateTimeKind.Utc, "what comes back out says what it is");
            row.Stamp.Should().Be(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
                "an unspecified value is taken at its word, not shifted by the machine's offset");
            row.NullableStamp!.Value.Should().Be(local.ToUniversalTime(),
                "a local one names a different instant, so it is converted");
        }
    }

    /// <summary>
    ///     The control: a value already in UTC is not touched.
    /// </summary>
    /// <remarks>
    ///     Without it, "the column holds UTC" is satisfied by a converter that shifts every value it
    ///     sees — which would round-trip perfectly and be wrong by the machine's offset.
    /// </remarks>
    [Fact]
    public async Task AValueAlreadyInUtc_IsUnchanged()
    {
        var (connection, options) = SqliteContextFactory.CreateOptions<InstantContext>(
            b => b.UsePragmaticTemporal());
        using var _ = connection;

        var moment = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var stamp = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        using (var db = new InstantContext(options))
        {
            await db.Database.EnsureCreatedAsync().ConfigureAwait(true);
            db.Entities.Add(new InstantEntity { Moment = moment, Stamp = stamp });
            await db.SaveChangesAsync().ConfigureAwait(true);
        }

        using (var db = new InstantContext(options))
        {
            var row = await db.Entities.SingleAsync().ConfigureAwait(true);

            row.Moment.Should().Be(moment);
            row.Stamp.Should().Be(stamp);
        }
    }

    /// <summary>
    ///     The third control: a converter somebody already chose is not replaced.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The audit log stores its instants as bigint ticks. A normalisation that overwrote that
    ///     converter would send a timestamptz at a bigint column and fail every write — with an error
    ///     naming the column, which points nowhere near the convention that caused it. A converter on a property is an
    ///     explicit decision, and a convention does not get to undo one.
    /// </remarks>
    [Fact]
    public async Task AConverterAlreadyChosen_IsLeftAlone()
    {
        var (connection, options) = SqliteContextFactory.CreateOptions<ExplicitConverterContext>(
            b => b.UsePragmaticTemporal());
        using var _ = connection;

        using var db = new ExplicitConverterContext(options);
        await db.Database.EnsureCreatedAsync().ConfigureAwait(true);

        var converter = db.Model
            .FindEntityType(typeof(InstantEntity))!
            .FindProperty(nameof(InstantEntity.Moment))!
            .GetValueConverter();

        converter.Should().BeSameAs(ExplicitConverterContext.Ticks,
            "the context chose how this column is stored, and the convention has no say over it");
    }

    /// <summary>
    ///     The second control: an application that says no keeps what it wrote.
    /// </summary>
    /// <remarks>
    ///     The rule is the framework's default, not a law. Without the opt-out an application that
    ///     deliberately stores wall-clock time would have no way to say so; without this test the
    ///     opt-out would be a flag nobody reads.
    /// </remarks>
    [Fact]
    public async Task WithNormalisationSwitchedOff_TheOffsetSurvives()
    {
        var (connection, options) = SqliteContextFactory.CreateOptions<OptOutInstantContext>(
            b => b.UsePragmaticTemporal(o => o.NormalizeInstantsToUtc = false));
        using var _ = connection;

        var written = new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.FromHours(2));

        using (var db = new OptOutInstantContext(options))
        {
            await db.Database.EnsureCreatedAsync().ConfigureAwait(true);
            db.Entities.Add(new InstantEntity { Moment = written });
            await db.SaveChangesAsync().ConfigureAwait(true);
        }

        using (var db = new OptOutInstantContext(options))
        {
            (await db.Entities.SingleAsync().ConfigureAwait(true)).Moment.Offset
                .Should().Be(TimeSpan.FromHours(2), "the application asked to keep it");
        }
    }
}
