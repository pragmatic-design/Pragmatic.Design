using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Npgsql;

namespace Pragmatic.Persistence.Sql.Explain;

/// <summary>
///     PostgreSQL: the statement is PREPAREd with its parameters as <c>$n</c> (typed from their context, no values), and
///     <c>EXPLAIN (FORMAT JSON) EXECUTE</c> with NULLs under <c>plan_cache_mode = force_generic_plan</c> answers the generic plan, the one
///     the prepared statement runs with whatever the values (<c>EXPLAIN (GENERIC_PLAN)</c> does the same for psql, but through the extended
///     protocol the driver binds the values, and a bound NULL folds the WHERE away). The plan tree is walked for a Seq Scan with a Filter (the
///     WHERE reads the whole table), a Sort node (the ordering is not served by an index) and a Nested Loop estimated over many rows.
/// </summary>
internal sealed class PostgresExplainer(string connectionString) : Explainer
{
    private const double WideLoopRows = 10_000;
    private const string Prepared = "__pragmatic_explain";
    private readonly NpgsqlConnection _connection = new(connectionString);
    private bool _generic;

    protected override DbConnection Connection => _connection;

    public override async Task<IReadOnlyList<Finding>> ExplainAsync(string statement)
    {
        var findings = new List<Finding>();
        if (!_generic)
        {
            await RunAsync("SET plan_cache_mode = force_generic_plan");
            _generic = true;
        }

        var prepared = false;
        try
        {
            await RunAsync($"PREPARE {Prepared} AS {Positional(statement, out var parameters)}");
            prepared = true;
            var arguments = parameters == 0 ? "" : "(" + string.Join(", ", Enumerable.Repeat("NULL", parameters)) + ")";
            using var command = _connection.CreateCommand();
            command.CommandText = $"EXPLAIN (FORMAT JSON) EXECUTE {Prepared}{arguments}";
            var json = (string?)await command.ExecuteScalarAsync();
            if (json is null)
                return findings;
            Trace(statement, json);
            using var document = JsonDocument.Parse(json);
            foreach (var root in document.RootElement.EnumerateArray())
                Walk(root.GetProperty("Plan"), findings);
        }
        catch (PostgresException e)
        {
            findings.Add(NotExplained(e));
        }
        finally
        {
            if (prepared)
                await RunAsync($"DEALLOCATE {Prepared}");
        }

        return findings;
    }

    private async Task RunAsync(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static void Walk(JsonElement node, List<Finding> findings)
    {
        var type = node.TryGetProperty("Node Type", out var nodeType) ? nodeType.GetString() : null;
        var rows = node.TryGetProperty("Plan Rows", out var planRows) ? planRows.GetDouble() : 0;
        switch (type)
        {
            case "Seq Scan" when node.TryGetProperty("Filter", out var filter):
                findings.Add(new Finding(Finding.SequentialScan, $"Seq Scan on {node.GetProperty("Relation Name").GetString()} with filter {filter.GetString()} (~{Rows(rows)} rows): no index serves the WHERE"));
                break;
            case "Sort":
                findings.Add(new Finding(Finding.Sort, $"Sort on {Keys(node)} (~{Rows(rows)} rows): no index serves the ORDER BY"));
                break;
            case "Nested Loop" when rows >= WideLoopRows:
                findings.Add(new Finding(Finding.WideNestedLoop, $"Nested Loop over ~{Rows(rows)} rows: a join without an index on the inner side"));
                break;
        }

        if (node.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray())
                Walk(child, findings);
        }
    }

    private static string Keys(JsonElement sort) => sort.TryGetProperty("Sort Key", out var keys) ? string.Join(", ", keys.EnumerateArray().Select(k => k.GetString())) : "?";

    private static string Rows(double rows) => rows.ToString("0", CultureInfo.InvariantCulture);
}
