using System.Data.Common;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Composition;
using Pragmatic.Migrations.Extensions;
using Pragmatic.Migrations.Introspection;

namespace Pragmatic.Migrations.Core.Tests.Unit.Configuration;

/// <summary>
///     Verifies the escape hatch: <c>ExcludeTable</c> keeps externally-owned tables out of
///     introspection so the diff engine never proposes to alter or drop them.
/// </summary>
public class ExcludeTableTests
{
    [Fact]
    public void ExcludeTable_AddsTablesToIntrospectorExclusions()
    {
        var services = new ServiceCollection();
        var builder = new PragmaticBuilderMock();
        builder.Services.Returns(services);

        builder.UsePragmaticMigrations(m => m.ExcludeTable("LegacyAudit", "ExtensionTable"));

        var introspector = services.BuildServiceProvider().GetRequiredService<ISchemaIntrospector>();
        introspector.ExcludedTables.Should().Contain("LegacyAudit").And.Contain("ExtensionTable");
    }

    [Fact]
    public async Task Introspect_ExcludedTable_IsNotReturned()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await Exec(conn, "CREATE TABLE \"Managed\" (\"Id\" TEXT PRIMARY KEY);");
        await Exec(conn, "CREATE TABLE \"External\" (\"Id\" TEXT PRIMARY KEY);");

        var introspector = new SqliteSchemaIntrospector { ExcludedTables = ["External"] };
        var schema = await introspector.IntrospectAsync(conn);

        schema.Tables.Should().Contain(t => t.Name == "Managed");
        schema.Tables.Should().NotContain(t => t.Name == "External");
    }

    private static async Task Exec(DbConnection conn, string sql)
    {
        var cmd = conn.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
