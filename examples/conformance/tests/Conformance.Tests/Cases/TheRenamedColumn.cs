using Conformance.Tests.Infrastructure;
using Npgsql;
using Pragmatic.Testing.Assertions;

namespace Conformance.Tests.Cases;

/// <summary>
///     A renamed column survives the migration, with its data inside.
/// </summary>
/// <remarks>
///     <para>
///         <c>[RenamedFrom("Price")]</c> on <c>CatalogItem.ListPrice</c> is what makes the difference
///         between a migration that <b>renames</b> and one that adds a new column and drops the old one.
///         Without it, the diff sees one column more and one less, and the second takes the data of every
///         row with it.
///     </para>
///     <para>
///         ⚠️ <b>On an empty database the declaration is inert</b>: the diff looks for the old column, does
///         not find it, and adds the new one as it would anyway. It is the setup in which an attribute seems
///         to work for years — examples that migrate databases without ever renaming anything. So here the
///         previous version is <b>built</b>: current schema, column brought back to the old name, a row
///         with a value, and then the migration.
///     </para>
///     <para>
///         On a database of its own, not the suite's: renaming a shared column while the other cases read it
///         would measure concurrency instead of the rename.
///     </para>
/// </remarks>
[Collection(ConformanceTestCollection.Name)]
[Trait("Category", "Conformance")]
public sealed class TheRenamedColumn(PostgresFixture fixture)
{
    private const string Item = "A row written before the rename";

    private static readonly Guid Id = Guid.NewGuid();

    [Fact]
    public async Task TheRename_KeepsTheData()
    {
        var database = await ADatabaseOfItsOwnAsync();

        await ConformanceSchema.ApplyAsync(database);
        await TheVersionBeforeTheRenameAsync(database);

        // The migration, as the host runs it at startup.
        await ConformanceSchema.ApplyAsync(database);

        (await ScalarAsync<decimal?>(database,
                $"""SELECT "ListPrice" FROM "CatalogItems" WHERE "PersistenceId" = '{Id}'"""))
            .Should().Be(42.50m,
                "the column was renamed and the row is the same: a drop+add would have left it at "
                + "zero, which is the defect [RenamedFrom] exists to avoid");
    }

    /// <summary>
    ///     The control: the row is the one from before, not one rewritten by the migration.
    /// </summary>
    /// <remarks>
    ///     Without it, «the value is there» would also be satisfied by a migration that recreates the table
    ///     — and by the case in which the value happens to be there. Here there is a single row and it
    ///     carries the text written before the migration.
    /// </remarks>
    [Fact]
    public async Task TheRow_IsTheOneFromBefore()
    {
        var database = await ADatabaseOfItsOwnAsync();

        await ConformanceSchema.ApplyAsync(database);
        await TheVersionBeforeTheRenameAsync(database);
        await ConformanceSchema.ApplyAsync(database);

        (await ScalarAsync<long>(database, """SELECT COUNT(*) FROM "CatalogItems" """))
            .Should().Be(1);
        (await ScalarAsync<string>(database,
                $"""SELECT "Name" FROM "CatalogItems" WHERE "PersistenceId" = '{Id}'"""))
            .Should().Be(Item);
    }

    /// <summary>
    ///     The control that says the rename happened: the old column is gone.
    /// </summary>
    /// <remarks>
    ///     A migration that added <c>ListPrice</c> next to <c>Price</c> would leave both, and the first case
    ///     would pass anyway if the value had ended up in the new one by another road.
    /// </remarks>
    [Fact]
    public async Task TheOldColumn_NoLongerExists()
    {
        var database = await ADatabaseOfItsOwnAsync();

        await ConformanceSchema.ApplyAsync(database);
        await TheVersionBeforeTheRenameAsync(database);
        await ConformanceSchema.ApplyAsync(database);

        (await ScalarAsync<long>(database,
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_name = 'CatalogItems' AND column_name = 'Price'
                """))
            .Should().Be(0, "it was renamed, not placed alongside");
    }

    /// <summary>The previous version's schema: the column under its old name, and a row inside.</summary>
    private static async Task TheVersionBeforeTheRenameAsync(string database)
    {
        await ExecuteAsync(database, """ALTER TABLE "CatalogItems" RENAME COLUMN "ListPrice" TO "Price" """);
        await ExecuteAsync(database,
            $"""
             INSERT INTO "CatalogItems" ("PersistenceId", "Name", "Price")
             VALUES ('{Id}', '{Item}', 42.50)
             """);
    }

    private async Task<string> ADatabaseOfItsOwnAsync()
    {
        var name = $"rename_{Guid.NewGuid():N}";

        await ExecuteAsync(fixture.ConnectionString, $"CREATE DATABASE \"{name}\"");

        return new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name }
            .ConnectionString;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
    {
        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = sql;
                var value = await command.ExecuteScalarAsync().ConfigureAwait(false);

                return value is null or DBNull ? default : (T)value;
            }
        }
    }
}
