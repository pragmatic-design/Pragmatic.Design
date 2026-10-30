#pragma warning disable CA2007 // ConfigureAwait in test code

using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Tests.Provider;

public abstract partial class SchemaScenarioTestBase
{
    // ===========================================================================================
    // Scenario 25: a name longer than the database allows is fitted to it — and converges
    // ===========================================================================================

    /// <summary>
    ///     The most characters the database allows in a name, as EF Core's provider declares it; null when no name the schema generates reaches it.
    /// </summary>
    protected virtual int? IdentifierLimit => null;

    // 70 characters: the index EF Core's configuration gives __OutboxMessages, the one a generated schema already has.
    private const string LongIndexName = "IX___OutboxMessages_ProcessedAt_NextAttemptAt_ClaimedUntil_CreatedAt";

    /// <summary>
    ///     An index whose declared name is over the database's limit is created under EF Core's truncated form of it (the first limit − 1
    ///     characters, then <c>~</c>), and the next run finds it there: the diff is empty, nothing is applied again.
    /// </summary>
    /// <remarks>
    ///     ⚠️ MySQL refused the declared name outright, so a schema with it could not be migrated. PostgreSQL accepts it and truncates it itself,
    ///     so the catalogue holds a name the schema does not declare.
    /// </remarks>
    [Fact]
    public virtual async Task Scenario25_AnIndexNameOverTheDatabasesLimit_IsFittedAndConverges()
    {
        await using var conn = await CreateConnectionAsync();
        var schema = MakeSchema(new TableSchema("Outbox", null,
            ImmutableArray.Create(
                new ColumnSchema("Id", PkType(), false, true),
                new ColumnSchema("ProcessedAt", IntType(), true, false),
                new ColumnSchema("NextAttemptAt", IntType(), true, false),
                new ColumnSchema("ClaimedUntil", IntType(), true, false),
                new ColumnSchema("CreatedAt", IntType(), false, false)),
            ImmutableArray.Create(new IndexSchema(LongIndexName, ImmutableArray.Create("ProcessedAt", "NextAttemptAt", "ClaimedUntil", "CreatedAt"))),
            []));

        var first = await ApplySchemaAsync(conn, schema);
        first.Success.Should().BeTrue(first.Error);

        var expected = IdentifierLimit is { } limit && LongIndexName.Length > limit ? LongIndexName[..(limit - 1)] + "~" : LongIndexName;
        var current = await CreateIntrospector().IntrospectAsync(conn);
        current.Tables.First(t => t.Name == "Outbox").Indexes.Select(i => i.Name).Should().Contain(expected);

        var again = await ApplySchemaAsync(conn, schema);
        again.Success.Should().BeTrue(again.Error);
        again.ChangesApplied.Should().Be(0, "the index the first run created is the one the schema declares, fitted to the database");
    }
}
