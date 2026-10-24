using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     Schema drift: <c>session.VerifySchemaAsync()</c> compares every registered entity (table, columns, kinds) with the database's
///     catalog. On the seeded databases the model and the schema agree; a column renamed behind the model's back is reported with its
///     name and the type the model expects, and the renamed one as unmapped.
/// </summary>
[Collection("databases")]
public class SchemaTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task The_seeded_schema_matches_the_model_and_a_renamed_column_is_reported(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var report = await session.VerifySchemaAsync();
        Assert.True(report.IsCompatible, report.ToString());
        Assert.DoesNotContain(report.Problems, p => p.Kind != SqlSchemaProblemKind.UnmappedColumn);
        Assert.True(report.Tables >= 20, report.Tables.ToString()); // the workload's entities and the test-only ones, views included

        // A column renamed in the database: the model's column missing (with the type it expects), the new one unmapped.
        await RenameAsync(session, dialect, "Reminders", "Text", "Body");
        try
        {
            var drifted = await session.VerifySchemaAsync();
            Assert.False(drifted.IsCompatible);
            var missing = Assert.Single(drifted.Problems, p => p.Kind == SqlSchemaProblemKind.MissingColumn);
            Assert.Equal(("Reminders", "Text"), (missing.Table, missing.Column));
            Assert.Equal(dialect switch { SqlDialect.Sqlite => "text", SqlDialect.PostgreSql => "text", SqlDialect.MySql => "varchar", SqlDialect.Oracle => "varchar2", _ => "nvarchar" }, missing.Expected);
            Assert.Contains("\"Reminders\".\"Text\" is not in the table (the model expects", missing.ToString(), StringComparison.Ordinal);
            var unmapped = Assert.Single(drifted.Problems, p => p.Kind == SqlSchemaProblemKind.UnmappedColumn && p.Table == "Reminders");
            Assert.Equal("Body", unmapped.Column);
        }
        finally
        {
            await RenameAsync(session, dialect, "Reminders", "Body", "Text");
        }

        Assert.True((await session.VerifySchemaAsync()).IsCompatible);
    }

    private static async Task RenameAsync(SqlSession session, SqlDialect dialect, string table, string from, string to)
    {
        using var command = session.Connection.CreateCommand();
        command.CommandText = dialect == SqlDialect.SqlServer ? $"EXEC sp_rename '[{table}].[{from}]', '{to}', 'COLUMN'" : $"ALTER TABLE \"{table}\" RENAME COLUMN \"{from}\" TO \"{to}\"";
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public void A_report_reads_as_a_list_of_problems()
    {
        var problem = new SqlSchemaProblem(SqlSchemaProblemKind.TypeMismatch, "Orders", "Total", "numeric", "text");
        Assert.Equal("\"Orders\".\"Total\" is text, the model expects numeric", problem.ToString());
        Assert.Equal("table \"Gone\" is not in the database", new SqlSchemaProblem(SqlSchemaProblemKind.MissingTable, "Gone", null, null, null).ToString());
    }
}
