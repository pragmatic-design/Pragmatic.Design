using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Pragmatic.Persistence.Sql.Explain;

/// <summary>
///     SQLite: <c>EXPLAIN QUERY PLAN</c> plans a statement with its parameters unbound (NULL). Its rows say <c>SCAN table</c> for a full
///     read (reported when the statement filters: a SCAN of a table read whole is the statement's intent), <c>SEARCH table USING INDEX</c>
///     for an index, <c>USE TEMP B-TREE FOR ORDER BY</c> for a sort no index serves. SQLite plans no wide nested loops to speak of.
/// </summary>
internal sealed class SqliteExplainer(string connectionString) : Explainer
{
    private readonly SqliteConnection _connection = new(connectionString);

    protected override DbConnection Connection => _connection;

    public override async Task<IReadOnlyList<Finding>> ExplainAsync(string statement)
    {
        var findings = new List<Finding>();
        var filtered = statement.Contains(" WHERE ", StringComparison.Ordinal);
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN " + WithoutLists(statement);
            using var reader = await command.ExecuteReaderAsync();
            var plan = new List<string>();
            while (await reader.ReadAsync())
            {
                var detail = reader.GetString(3); // id, parent, notused, detail
                plan.Add(detail);
                if (filtered && detail.StartsWith("SCAN ", StringComparison.Ordinal) && !detail.Contains("USING", StringComparison.Ordinal) && !detail.Contains("SUBQUERY", StringComparison.Ordinal))
                    findings.Add(new Finding(Finding.SequentialScan, $"{detail}: the WHERE reads the whole table, no index serves it"));
                else if (detail.StartsWith("USE TEMP B-TREE FOR ORDER BY", StringComparison.Ordinal))
                    findings.Add(new Finding(Finding.Sort, $"{detail}: no index serves the ORDER BY"));
            }

            Trace(statement, string.Join("\n", plan));
        }
        catch (SqliteException e)
        {
            findings.Add(NotExplained(e));
        }

        return findings;
    }
}
