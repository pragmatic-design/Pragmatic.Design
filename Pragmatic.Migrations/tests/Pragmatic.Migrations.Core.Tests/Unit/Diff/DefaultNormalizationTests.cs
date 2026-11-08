using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Core.Tests.Unit.Diff;

/// <summary>
///     A default read back from PostgreSQL carries its cast (<c>'IT'::character varying</c>, <c>'{}'::text</c>) while the desired schema
///     writes the bare literal. Compared as they come, the two never matched: an <c>ALTER DEFAULT</c> ran at every start, and a host waiting
///     for the migration leader was refused as "still N change(s) behind".
/// </summary>
public class DefaultNormalizationTests
{
    private readonly ISchemaDiffEngine _engine = new SchemaDiffEngine();

    [Theory]
    [InlineData("'IT'", "'IT'::character varying")]
    [InlineData("'{}'", "'{}'::text")]
    [InlineData("'{}'", "'{}'::text[]")]
    [InlineData("'[]'", "'[]'::jsonb")]
    [InlineData("'Europe/Rome'", "'Europe/Rome'::character varying(64)")]
    [InlineData("0", "(0)::numeric")]
    public void A_default_read_back_with_its_cast_is_the_same_default(string desired, string readBack)
        => Diff(desired, readBack).Should().BeEmpty();

    /// <summary>The control: a default that differs after the cast is still a change.</summary>
    [Fact]
    public void A_default_that_differs_beyond_the_cast_is_a_change()
        => Diff("'IT'", "'FR'::character varying").Should().ContainSingle();

    /// <summary>A cast inside the expression is part of it, not a trailing one: a sequence default stays as written.</summary>
    [Fact]
    public void A_cast_inside_a_function_call_is_kept()
        => SchemaDiffEngine.NormalizeDefault("nextval('orders_id_seq'::regclass)").Should().Be("nextval('orders_id_seq'::regclass)");

    /// <summary>A text whose value contains <c>::</c> is a value, not a cast.</summary>
    [Fact]
    public void A_double_colon_inside_a_string_is_kept()
        => SchemaDiffEngine.NormalizeDefault("'a::b'").Should().Be("'a::b'");

    private ImmutableArray<SchemaChange> Diff(string desiredDefault, string currentDefault)
    {
        var desired = Schema(desiredDefault);
        var current = Schema(currentDefault);
        return _engine.ComputeDiff(desired, current).Changes.Where(c => c is AlterColumnDefault).ToImmutableArray();
    }

    private static SchemaVersion Schema(string defaultValue) => new(ImmutableArray.Create(
        new TableSchema("Organizations", null,
            ImmutableArray.Create(
                new ColumnSchema("Id", "uuid", false, true),
                new ColumnSchema("Country", "character varying(2)", false, false, defaultValue)),
            [], [])));
}
