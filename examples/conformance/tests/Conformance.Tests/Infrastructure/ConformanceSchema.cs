using Npgsql;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Sql;

namespace Conformance.Tests.Infrastructure;

/// <summary>
///     Conformance's schema, applied to an empty database.
/// </summary>
/// <remarks>
///     The snapshot Pragmatic migrations generate from the entities — no EF Core migration, and no
///     hand-written script that could diverge from the entities. Shared by the fixtures that start a
///     container: each needs it, and a copy per fixture is a copy that ages.
/// </remarks>
public static class ConformanceSchema
{
    public static async Task ApplyAsync(string connectionString)
    {
        var introspector = new PostgreSqlSchemaIntrospector();
        var diffEngine = new SchemaDiffEngine();
        var sqlGenerator = new PostgreSqlMigrationGenerator();

        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);

            var desired = Conformance.ConformanceDatabaseSchema.Current;
            var current = await introspector.IntrospectAsync(connection).ConfigureAwait(false);
            var diff = diffEngine.ComputeDiff(desired, current);

            if (!diff.HasChanges)
                return;

            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = sqlGenerator.GenerateScript(diff);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }
}
