using Pragmatic.Migrations.Schema;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A key the database assigns (<c>[Identity]</c>) in the schema the generator writes for a <c>[SqlDatabase]</c>: the column is the dialect's
///     identity, so the database migrated from that schema fills it, the engine's insert reads it back, and a second migration finds the identity
///     it declared, not a type change.
/// </summary>
[Collection("pragmatic databases")]
public class IdentityKeyTests(PragmaticDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task An_identity_key_is_assigned_by_the_migrated_database_and_converges(SqlDialect dialect)
    {
        // The generated table alone: the one database of an assembly holds every entity in it, the suite's Pragmatic entities too, whose own schema
        // (DefaultSchema) the other tests migrate (#421).
        var generated = TicketDatabase.Schema(dialect);
        var schema = new SchemaVersion([generated.Tables.Single(t => t.Name == "Tickets")], generated.DatabaseName, generated.ProviderName, generated.ConfigKey);
        var connectionString = await databases.CreateAsync(dialect, "identity");

        var created = await PragmaticMigrations.MigrateAsync(connectionString, schema);
        Assert.True(created.Success, created.Error + "\n" + created.GeneratedSql);

        await using (var connection = await PragmaticMigrations.OpenAsync(dialect, connectionString))
        {
            using var session = new SqlSession(connection, dialect);
            var first = new Ticket { Subject = "first" };
            await session.InsertAsync(first);
            var more = Enumerable.Range(0, 3).Select(i => new Ticket { Subject = "t" + i }).ToList();
            await session.InsertManyAsync(more);

            Assert.True(first.Id > 0, $"the first key read back is {first.Id}");
            Assert.Equal(4, more.Select(t => t.Id).Append(first.Id).Distinct().Count());
            Assert.Equal(4, await session.Query<Ticket>().CountAsync());
        }

        var diff = await PragmaticMigrations.DiffAsync(dialect, connectionString, schema);
        Assert.True(diff.IsEmpty, "left to do: " + string.Join("; ", diff.Select(c => c.Description)));
        var again = await PragmaticMigrations.MigrateAsync(connectionString, schema);
        Assert.True(again.Success, again.Error);
        Assert.Equal(0, again.ChangesApplied);
    }
}
